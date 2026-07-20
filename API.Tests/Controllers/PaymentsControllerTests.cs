using System.Security.Claims;
using System.Text;
using API.Controllers;
using API.Core;
using API.DTOs;
using API.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Stripe;
using Xunit;

namespace API.Tests.Controllers;

public class PaymentsControllerTests
{
    private readonly IPaymentService _service = Substitute.For<IPaymentService>();

    private PaymentsController CreateController(string? userId = "winner", HttpContext? httpContext = null)
    {
        var controller = new PaymentsController(_service, Substitute.For<ILogger<PaymentsController>>());
        httpContext ??= new DefaultHttpContext();
        if (userId != null)
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId)], "test"));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Fact]
    public async Task CreateCheckoutSession_WithoutUserIdClaim_ReturnsUnauthorized()
    {
        var controller = CreateController(userId: null);

        var result = await controller.CreateCheckoutSession(1);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task CreateCheckoutSession_OnSuccess_ReturnsOkWithDto()
    {
        var dto = new CreatePaymentResponseDto { CheckoutUrl = "https://checkout.stripe.test/pay/cs_1" };
        _service.CreateCheckoutSession(1, "winner").Returns(Result<CreatePaymentResponseDto>.Success(dto));
        var controller = CreateController();

        var result = await controller.CreateCheckoutSession(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(dto, ok.Value);
    }

    [Theory]
    [InlineData(FailureReason.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(FailureReason.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(FailureReason.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(FailureReason.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(FailureReason.InternalError, StatusCodes.Status500InternalServerError)]
    public async Task CreateCheckoutSession_OnFailure_MapsReasonToStatusCode(FailureReason reason, int expected)
    {
        _service.CreateCheckoutSession(1, "winner")
            .Returns(Result<CreatePaymentResponseDto>.Failure("nope", reason));
        var controller = CreateController();

        var result = await controller.CreateCheckoutSession(1);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(expected, obj.StatusCode);
    }

    [Fact]
    public async Task GetStatus_OnSuccess_ReturnsOkWithDto()
    {
        var dto = new PaymentStatusDto { Status = "Paid", Amount = 150m };
        _service.GetPaymentStatus(1, "winner").Returns(Result<PaymentStatusDto>.Success(dto));
        var controller = CreateController();

        var result = await controller.GetStatus(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(dto, ok.Value);
    }

    // GetStatus shares HandleFailure with CreateCheckoutSession, so this confirms wiring — that a
    // failed Result is routed through HandleFailure rather than returned as Ok — not the mapping itself.
    [Theory]
    [InlineData(FailureReason.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(FailureReason.Forbidden, StatusCodes.Status403Forbidden)]
    public async Task GetStatus_OnFailure_MapsReasonToStatusCode(FailureReason reason, int expected)
    {
        _service.GetPaymentStatus(1, "winner").Returns(Result<PaymentStatusDto>.Failure("nope", reason));
        var controller = CreateController();

        var result = await controller.GetStatus(1);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(expected, obj.StatusCode);
    }

    [Fact]
    public async Task Webhook_OnSuccess_ReturnsOk()
    {
        var controller = CreateController(httpContext: HttpContextWithBody("{}", signature: "t=1,v1=abc"));

        var result = await controller.Webhook();

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task Webhook_WhenServiceThrowsStripeException_ReturnsBadRequest()
    {
        _service.When(s => s.HandleWebhook(Arg.Any<string>(), Arg.Any<string>()))
            .Do(_ => throw new StripeException("signature verification failed"));
        var controller = CreateController(httpContext: HttpContextWithBody("{}", signature: "t=1,v1=bad"));

        var result = await controller.Webhook();

        Assert.IsType<BadRequestResult>(result);
    }

    // Characterization test pinning intentional behavior: only StripeException (bad signature) is
    // translated to 400. Any other exception propagates so ExceptionMiddleware turns it into a 5xx —
    // which is what makes Stripe RETRY the delivery. Wrapping the whole body in a catch-all that
    // returned Ok()/BadRequest() would silently drop retryable events; this test guards against that.
    [Fact]
    public async Task Webhook_WhenServiceThrowsNonStripeException_PropagatesForA5xxRetry()
    {
        _service.When(s => s.HandleWebhook(Arg.Any<string>(), Arg.Any<string>()))
            .Do(_ => throw new InvalidOperationException("webhook secret not configured"));
        var controller = CreateController(httpContext: HttpContextWithBody("{}", signature: "t=1,v1=abc"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Webhook());
    }

    private static DefaultHttpContext HttpContextWithBody(string body, string signature)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        ctx.Request.Headers["Stripe-Signature"] = signature;
        return ctx;
    }
}
