using System.Security.Claims;
using API.DTOs;
using API.Entities;
using API.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;


public class BidController(IBidService bidService) : BaseApiController
{
    [HttpGet("/api/auctions/{auctionId}/bids")]
    public async Task<IActionResult> GetBids(int auctionId)
    {
        var bids = await bidService.GetAllBids(auctionId);
        return Ok(bids);
    }

    [HttpPost("/api/auctions/{auctionId}/bids")]
    [Authorize]
    public async Task<ActionResult<BidResponseDto>> PlaceBid([FromBody] BidRequestDto bidRequestDto, int auctionId)
    {
        var user = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(user)) return Unauthorized();

        var res = await bidService.PlaceBid(bidRequestDto, auctionId, user);
        if (!res.IsSuccess)
            return HandleFailure(res);

        return Ok(res.Value);
    }
}
