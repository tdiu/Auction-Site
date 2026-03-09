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
    public async Task<ActionResult<IReadOnlyList<AuctionResponseDto>>> GetAllAuctions(
        [FromQuery] string? displayName, 
        [FromQuery] string? searchTerm)
    {
        var query = auctionRepository.GetAuctionsQueryable();

        if (!string.IsNullOrEmpty(displayName))
        {
            query = query.Where(a => a.Seller.DisplayName == displayName);
        }

        if (!string.IsNullOrEmpty(searchTerm))
        {
            query = query.Where(a => a.ItemName.ToLower().Contains(searchTerm.ToLower()));
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
    [HttpPost]
    public async Task<ActionResult<AuctionResponseDto>> CreateAuction(AuctionRequestDto auctionRequestDto)
    {
        if (auctionRequestDto.BuyNowPrice.HasValue && auctionRequestDto.BuyNowPrice.Value < auctionRequestDto.StartingPrice)
        {
            return BadRequest("Buy Now price must be at least the starting price.");
        }

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