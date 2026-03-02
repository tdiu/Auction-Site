using System.Data;
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
    public async Task<ActionResult<IReadOnlyList<AuctionResponseDto>>> GetAllAuctions()
    {
        return await context.Auctions
            .Select(auction => new AuctionResponseDto
            {
                AuctionId = auction.AuctionId,
                ItemName = auction.ItemName,
                StartingPrice = auction.StartingPrice,
                BuyNowPrice = auction.BuyNowPrice,
                SellerId = auction.SellerId,
                SellerName = auction.Seller.DisplayName,
                StartTime = auction.StartTime,
                EndTime = auction.EndTime
            }).ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Auction>> GetAuction(int id)
    {
        var auction = await context.Auctions.FindAsync(id);
        if (auction == null) return NotFound();
        return auction;
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
        return auction.ToDto();
    }
}