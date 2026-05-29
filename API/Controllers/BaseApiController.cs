using API.Core;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class BaseApiController : Controller
{
    protected ActionResult HandleFailure<T>(Result<T> result)
    {
        return result.Reason switch
        {
            FailureReason.NotFound => NotFound(result.Error),
            FailureReason.Unauthorized => Unauthorized(result.Error),
            FailureReason.Conflict => Conflict(result.Error),
            FailureReason.Validation => BadRequest(result.Error),
            _ => StatusCode(500, result.Error)
        };
    }
}
