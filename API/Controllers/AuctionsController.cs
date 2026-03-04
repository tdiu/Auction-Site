using System.Security.Claims;
using API.Data;
using API.DTOs;
using API.Entities;
using API.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API.Controllers;

public class AuctionsController(AppDbContext context) : BaseApiController
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AuctionResponseDto>>> GetAllAuctions([FromQuery] string? userId)
    {
        var query = context.Auctions.AsQueryable();
        if (!string.IsNullOrEmpty(userId))
        {
            query = query.Where(a => a.SellerId == userId);
        }
        return await query.ProjectToDto().ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AuctionResponseDto>> GetAuction(int id)
    {
        var auction = await context.Auctions
            .ProjectToDto()
            .FirstOrDefaultAsync(a => a.AuctionId == id);
        
        return auction == null ? NotFound() : auction;
    }

    [Authorize]
    [HttpPost("sell")]
    public async Task<ActionResult<AuctionResponseDto>> CreateAuction(AuctionRequestDto auctionRequestDto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        
        var currTime = DateTimeOffset.UtcNow;
        
        var auction = new Auction()
        {
            ItemName = auctionRequestDto.ItemName,
            StartingPrice = auctionRequestDto.StartingPrice,
            BuyNowPrice = auctionRequestDto.BuyNowPrice,
            SellerId = userId,
            StartTime = currTime,
            EndTime = currTime.AddDays(7)
        };
        
        context.Auctions.Add(auction);
        await context.SaveChangesAsync();

        var responseDto = await context.Auctions
            .ProjectToDto()
            .FirstOrDefaultAsync(a => a.AuctionId == auction.AuctionId);

        if (responseDto == null) return NotFound();
        return responseDto;
    }
}