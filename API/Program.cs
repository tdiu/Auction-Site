using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using API.Data;
using API.Entities;
using API.Extensions;
using API.Interfaces;
using API.Middleware;
using API.Services;
using API.Services.Email;
using API.Services.Outbox;
using API.Services.Outbox.Handlers;
using API.SignalR;
using Dapper;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Stripe;
using TokenService = API.Services.TokenService;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql((builder.Configuration.GetConnectionString("DefaultConnection")));
});
builder.Services.AddCors();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAuctionRepository, AuctionRepository>();
builder.Services.AddScoped<IAuctionService, AuctionService>();
builder.Services.AddScoped<IBidRepository, BidRepository>();
builder.Services.AddScoped<IMessageRepository, MessageRepository>();
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IBidService, BidService>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IOutboxRepository, OutboxRepository>();
builder.Services.AddScoped<IRefreshSessionRepository, RefreshSessionRepository>();
builder.Services.AddScoped<IOutboxHandler, PaymentCompletedHandler>();
builder.Services.AddScoped<IOutboxHandler, PaymentReceiptHandler>();
builder.Services.AddScoped<IOutboxHandler, AuctionEndedHandler>();
builder.Services.AddScoped<AuctionSettlementJob>();
builder.Services.AddScoped<SessionSweepJob>();
builder.Services.AddScoped<OutboxDispatcher>();
builder.Services.AddScoped<IEmailTemplateRenderer, RazorEmailTemplateRenderer>();
builder.Services.AddRazorTemplating();
// Dev-only: the one implementation targets a local SMTP catcher. Other environments resolve no
// IEmailSender, so every outbox handler fails to construct. Needs an else before deploying.
if (builder.Environment.IsDevelopment())
    builder.Services.AddScoped<IEmailSender, MailKitEmailSender>();
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddProblemDetails();
builder.Services.AddSignalR();
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(
        builder.Configuration.GetConnectionString("DefaultConnection"))));
builder.Services.AddHangfireServer();
builder.Services.AddSingleton(_ =>
    new StripeClient(builder.Configuration["Stripe:SecretKey"]
                     ?? throw new InvalidOperationException()));
builder.Services.AddIdentityCore<AppUser>(options =>
    {
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

var authBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var tokenKey = builder.Configuration["TokenKey"] ?? throw new Exception("TokenKey not found");
        options.TokenValidationParameters = new TokenValidationParameters()
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tokenKey)),
            ValidateIssuer = false,
            ValidateAudience = false,
        };
        options.Events = new JwtBearerEvents()
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

authBuilder.AddCookie(IdentityConstants.ExternalScheme, options =>
{
    options.Cookie.Name = IdentityConstants.ExternalScheme;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
    options.SlidingExpiration = false;
});

var googleClientId = builder.Configuration["Google:ClientId"];
var googleClientSecret = builder.Configuration["Google:ClientSecret"];

if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.CallbackPath = "/api/signin-google";
        options.SaveTokens = false;
        options.ClaimActions.MapJsonKey("email_verified", "email_verified", "boolean");
    });
}

builder.Services.Configure<ApiBehaviorOptions>(options =>
    options.InvalidModelStateResponseFactory = actionContext =>
    {
        var errors = actionContext.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => ToCamelCase(entry.Key),
                entry => entry.Value!.Errors
                    .Select(error => string.IsNullOrEmpty(error.ErrorMessage)
                        ? "The input was not valid."
                        : error.ErrorMessage)
                    .ToArray());

        var problemDetails = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation Failed",
            Detail = "One or more validation errors occurred"
        };
        return new BadRequestObjectResult(problemDetails);
    });

var authWindow = TimeSpan.FromMinutes(15);
const int authSegmentsPerWindow = 3;
var authRetryAfter = authWindow / authSegmentsPerWindow;

// Volume protection only; the per-account lockout stops guessing. Keyed on IP, so a tight number
// costs a NAT'd office more than an attacker.
const int loginPermitLimit = 30;
const int registerPermitLimit = 10;

RateLimitPartition<string> AuthPartition(string policy, int permitLimit, HttpContext httpContext)
{
    var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    return RateLimitPartition.GetSlidingWindowLimiter(
        $"{policy}:{ip}",
        _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = authWindow,
            SegmentsPerWindow = authSegmentsPerWindow,
            QueueLimit = 0
        });
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        if (RateLimitPartitions.IsExempt(httpContext.Request.Path))
            return RateLimitPartition.GetNoLimiter(RateLimitPartitions.Exempt);

        var bucket = RateLimitPartitions.For(httpContext);

        return RateLimitPartition.GetFixedWindowLimiter(bucket.Key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = bucket.PermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });

    options.AddPolicy("login", ctx => AuthPartition("login", loginPermitLimit, ctx));
    options.AddPolicy("register", ctx => AuthPartition("register", registerPermitLimit, ctx));

    options.OnRejected = (context, _) =>
    {
        // Only the fixed-window limiters populate this. SlidingWindowRateLimiter leaves it unset,
        // which would otherwise send a bare 429 with no back-off hint from the auth endpoints.
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var metadata)
            ? metadata
            : authRetryAfter;

        context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        return ValueTask.CompletedTask;
    };
});

// Every per-IP limit above reads Connection.RemoteIpAddress, which behind a proxy is the proxy itself.
// Without this, all anonymous traffic collapses into one partition.
var knownProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
var knownNetworks = builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];
var trustForwardedHeaders = knownProxies.Length > 0 || knownNetworks.Length > 0;

if (trustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // Only the nearest hop is ours to trust; anything further left in the chain is client-supplied.
        options.ForwardLimit = 1;
        // Defaults trust loopback, which is not where the proxy lives once this is containerised.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var proxy in knownProxies)
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        foreach (var network in knownNetworks)
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    });
}

builder.Services.AddHttpsRedirection(options =>
{
    options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
    options.HttpsPort = 5001;
});
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
});
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.

// First in the pipeline: everything downstream that reads the scheme or the client IP
// (HSTS, HTTPS redirect, the rate limiter) needs the rewritten values, not the proxy's.
if (trustForwardedHeaders)
    app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
if (app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseMiddleware<ExceptionMiddleware>();
app.UseCors(x => x
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
    .WithOrigins("http://localhost:4200", "https://localhost:4200"));

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireDashboardAuthFilter(app.Environment)]
});

app.MapControllers();
app.MapHub<PresenceHub>("/hubs/presence");

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        var userManager = services.GetRequiredService<UserManager<AppUser>>();
        await DbInitializer.SeedData(context, userManager);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred during database seeding");
    }
}

// Recurring-job registration is separate from seeding: a failure here means no settlement
// sweep and no outbox dispatch, which must not be reported as (or masked by) a seeding error.
try
{
    var recurring = app.Services.GetRequiredService<IRecurringJobManager>();
    recurring.AddOrUpdate<AuctionSettlementJob>(
        "auction-settlement",
        j => j.RunAsync(CancellationToken.None),
        builder.Configuration["Settlement:SweepCron"] ?? Cron.Minutely());
    recurring.AddOrUpdate<OutboxDispatcher>(
        "outbox-dispatch",
        d => d.DispatchAsync(CancellationToken.None),
        builder.Configuration["Outbox:DispatchCron"] ?? Cron.Minutely());
    recurring.AddOrUpdate<SessionSweepJob>(
        "session-sweep",
        j => j.RunAsync(CancellationToken.None),
        builder.Configuration["Session:SweepCron"] ?? Cron.Minutely());
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "Failed to register recurring Hangfire jobs; settlement and outbox dispatch will not run");
}

app.Run();

static string ToCamelCase(string value)
{
    if (string.IsNullOrEmpty(value)) return value;

    var lastSeparatorIndex = value.LastIndexOf('.');
    var fieldName = lastSeparatorIndex >= 0 ? value[(lastSeparatorIndex + 1)..] : value;

    if (string.IsNullOrEmpty(fieldName) || char.IsLower(fieldName[0])) return fieldName;

    return char.ToLowerInvariant(fieldName[0]) + fieldName[1..];
}
