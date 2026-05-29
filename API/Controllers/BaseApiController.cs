using API.Core;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class BaseApiController : Controller
{
    protected ActionResult HandleFailure<T>(Result<T> result)
    {
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
}
