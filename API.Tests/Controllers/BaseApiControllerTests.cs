using API.Controllers;
using API.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace API.Tests.Controllers;

public class BaseApiControllerTests
{
    [Fact]
    public void HandleFailure_WithValidationErrors_ReturnsValidationProblemDetails()
    {
        var controller = new TestController();
        var result = Result<string>.ValidationFailure(new Dictionary<string, string[]>
        {
            ["DisplayName"] = ["Username already exists"],
            ["password"] = ["Passwords must be at least 6 characters."]
        });

        var actionResult = controller.Handle(result);

        var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("Validation Failed", problem.Title);
        Assert.Contains("Username already exists", problem.Errors["displayName"]);
        Assert.Contains("Passwords must be at least 6 characters.", problem.Errors["password"]);
    }

    private class TestController : BaseApiController
    {
        public ActionResult Handle<T>(Result<T> result) => HandleFailure(result);
    }
}
