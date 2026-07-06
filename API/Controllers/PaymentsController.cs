using System.Security.Claims;
using API.DTOs;
using API.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace API.Controllers;

[Authorize]
public class PaymentsController(IPaymentService paymentService, ILogger<PaymentsController> logger) : BaseApiController
{
    [HttpPost("{auctionId:int}/checkout-session")]
    public async Task<ActionResult<CreatePaymentResponseDto>> CreateCheckoutSession(int auctionId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null)
            return Unauthorized();

        var res = await paymentService.CreateCheckoutSession(auctionId, userId);
        return res.IsSuccess ? Ok(res.Value) : HandleFailure(res);
    }

    [HttpGet("{auctionId:int}/status")]
    public async Task<ActionResult<PaymentStatusDto>> GetStatus(int auctionId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null)
            return Unauthorized();

        var res = await paymentService.GetPaymentStatus(auctionId, userId);
        return res.IsSuccess ? Ok(res.Value) : HandleFailure(res);
    }

    [AllowAnonymous]
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        var json = await new StreamReader(Request.Body).ReadToEndAsync();
        var signature = Request.Headers["Stripe-Signature"].ToString();

        try
        {
            await paymentService.HandleWebhook(json, signature);
            return Ok();
        }
        catch (StripeException e)
        {
            logger.LogWarning(e, "Stripe webhook signature verification failed");
            return BadRequest();
        }
    }
}
