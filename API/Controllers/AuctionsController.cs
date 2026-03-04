using System.Security.Claims;
using API.Data;
using API.DTOs;
using API.Entities;
using API.Extensions;
using API.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API.Controllers;

public class AuctionsController(AppDbContext context, IAuctionRepository auctionRepository) : BaseApiController
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AuctionResponseDto>>> GetAllAuctions([FromQuery] string? displayName)
    {
        var query = auctionRepository.GetAuctionsQueryable();
        if (!string.IsNullOrEmpty(displayName))
        {
            query = query.Where(a => a.Seller.DisplayName == displayName);
        }
        return await query.ProjectToDto().ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AuctionResponseDto>> GetAuction(int id)
    {
        var auction = await auctionRepository.GetAuctionAsync(id);
        
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
        
        await auctionRepository.CreateAuctionAsync(auction);
        var responseDto = await auctionRepository.GetAuctionAsync(auction.AuctionId);

        if (responseDto == null) return NotFound();
        return responseDto;
    }
}