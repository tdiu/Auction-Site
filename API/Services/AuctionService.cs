using System.Security.Claims;
using API.Core;
using API.DTOs;
using API.Entities;
using API.Extensions;
using API.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API.Services;

public class AuctionService(IAuctionRepository auctionRepository) : IAuctionService
{
    public async Task<IReadOnlyList<AuctionResponseDto>> GetAllAuctions(string? displayName, string? searchTerm)
    {
        var query = auctionRepository.GetAuctionsQueryable();
        // Filter by queries
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

    public async Task<Result<AuctionResponseDto>> GetAuctionById(int id)
    {
        var auction = await auctionRepository.GetAuctionAsync(id);
        return auction == null ? Result<AuctionResponseDto>.Failure("Auction not found") :  Result<AuctionResponseDto>.Success(auction.ToDto());
    }

    public async Task<Result<AuctionResponseDto>> CreateAuction(AuctionRequestDto auctionRequestDto, string userId)
    {
        if (auctionRequestDto.BuyNowPrice.HasValue && auctionRequestDto.BuyNowPrice.Value < auctionRequestDto.StartingPrice)
        {
            return Result<AuctionResponseDto>.Failure("Buy Now Price cannot be set below starting price");
        }
        
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
        var response = await auctionRepository.GetAuctionAsync(auction.AuctionId);
        return response == null ? Result<AuctionResponseDto>.Failure("Not Found") :  Result<AuctionResponseDto>.Success(response.ToDto());
    }
}