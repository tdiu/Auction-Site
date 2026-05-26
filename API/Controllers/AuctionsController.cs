using System.Security.Claims;
using API.DTOs;
using API.Entities;
using API.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

public class AuctionsController(IAuctionService auctionService) : BaseApiController
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AuctionResponseDto>>> GetAllAuctions(
        [FromQuery] string? displayName,
        [FromQuery] string? searchTerm,
        [FromQuery] AuctionStatus? status)
    {
        var auctions = await auctionService.GetAllAuctions(displayName, searchTerm, status);
        return Ok(auctions);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AuctionResponseDto>> GetAuction(int id)
    {
        var auction = await auctionService.GetAuctionById(id);
        if (!auction.IsSuccess) return NotFound();

        return Ok(auction.Value);
    }

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<AuctionResponseDto>> CreateAuction(AuctionRequestDto auctionRequestDto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var res = await auctionService.CreateAuction(auctionRequestDto, userId);
        if (!res.IsSuccess) return BadRequest(res.Error);
        return Ok(res.Value);
    }
}
