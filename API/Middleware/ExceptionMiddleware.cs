using System.Net;
using Microsoft.AspNetCore.Mvc;

namespace API.Middleware;

public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger,
    IHostEnvironment env, IProblemDetailsService problemDetailsService)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Message}", ex.Message);
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var problem = new ProblemDetails
            {
                Status = (int)HttpStatusCode.InternalServerError,
                Title = "An unexpected error has occurred",
                Detail = env.IsDevelopment() ? ex.Message : "An internal error has occurred",
            };

            if (env.IsDevelopment())
            {
                problem.Extensions["stackTrace"] = ex.StackTrace;
            }

            await problemDetailsService.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = problem
            });
        }
    }
}
