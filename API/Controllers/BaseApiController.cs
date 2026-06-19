using API.Core;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class BaseApiController : Controller
{
    protected ActionResult HandleFailure<T>(Result<T> result)
    {
        if (result.Reason == FailureReason.Validation && result.ValidationErrors?.Count > 0)
        {
            return BadRequest(new ValidationProblemDetails(NormalizeValidationKeys(result.ValidationErrors))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation Failed",
                Detail = "One or more validation errors occurred"
            });
        }

        var statusCode = result.Reason switch
        {
            FailureReason.NotFound => StatusCodes.Status404NotFound,
            FailureReason.Unauthorized => StatusCodes.Status401Unauthorized,
            FailureReason.Conflict => StatusCodes.Status409Conflict,
            FailureReason.Validation => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };

        return Problem(detail: result.Error, statusCode: statusCode);
    }

    private static Dictionary<string, string[]> NormalizeValidationKeys(Dictionary<string, string[]> errors)
    {
        return errors.ToDictionary(error => ToCamelCase(error.Key), error => error.Value);
    }

    private static string ToCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value) || char.IsLower(value[0])) return value;

        return char.ToLowerInvariant(value[0]) + value[1..];
    }
}
