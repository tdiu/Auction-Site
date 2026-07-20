using Hangfire.Dashboard;

namespace API.Extensions;

public class HangfireDashboardAuthFilter(IWebHostEnvironment env) : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        if (env.IsDevelopment())
            return true;

        var http = context.GetHttpContext();
        return http.User.Identity?.IsAuthenticated == true && http.User.IsInRole("Admin");
    }
}
