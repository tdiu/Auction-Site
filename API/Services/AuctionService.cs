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
    public async Task<PagedList<AuctionResponseDto>> GetAllAuctions(
        string? displayName,
        string? searchTerm,
        AuctionStatus? status,
        int page,
        int pageSize)
    {
        var query = auctionRepository.GetAuctionsQueryable();
        var now = DateTimeOffset.UtcNow;

        // Default to Active if no status is specified
        var filterStatus = status ?? AuctionStatus.Active;

        query = filterStatus switch
        {
            AuctionStatus.Active => query.Where(a => a.EndTime > now),
            AuctionStatus.Expired => query.Where(a => a.EndTime <= now && a.CurrentHighBid == null),
            AuctionStatus.Ended => query.Where(a => a.EndTime <= now && a.CurrentHighBid != null),
            _ => query.Where(a => a.EndTime > now)
        };

        // Filter by queries
        if (!string.IsNullOrEmpty(displayName))
        {
            query = query.Where(a => a.Seller.DisplayName == displayName);
        }
        if (!string.IsNullOrEmpty(searchTerm))
        {
            query = query.Where(a => a.ItemName.ToLower().Contains(searchTerm.ToLower()));
        }

        // TODO: Add sort options
        query = query.OrderByDescending(a => a.StartTime);
        return await PagedList<AuctionResponseDto>.CreateAsync(query.ProjectToDto(now), page, pageSize);
    }

    public async Task<Result<AuctionResponseDto>> GetAuctionById(int id)
    {
        var auction = await auctionRepository.GetAuctionAsync(id);
        return auction == null
            ? Result<AuctionResponseDto>.Failure("Auction not found", FailureReason.NotFound)
            : Result<AuctionResponseDto>.Success(auction.ToDto(DateTimeOffset.UtcNow));
    }

    public async Task<Result<AuctionResponseDto>> CreateAuction(AuctionRequestDto auctionRequestDto, string userId)
    {
        var itemName = auctionRequestDto.ItemName.Trim();
        if (string.IsNullOrEmpty(itemName))
            return Result<AuctionResponseDto>.Failure("Item name is required", FailureReason.Validation);


        if (auctionRequestDto.BuyNowPrice.HasValue && auctionRequestDto.BuyNowPrice.Value < auctionRequestDto.StartingPrice)
            return Result<AuctionResponseDto>.Failure("Buy Now Price cannot be set below starting price", FailureReason.Validation);


        var currTime = DateTimeOffset.UtcNow;
        var auction = new Auction()
        {
            ItemName = itemName,
            StartingPrice = auctionRequestDto.StartingPrice,
            BuyNowPrice = auctionRequestDto.BuyNowPrice,
            SellerId = userId,
            StartTime = currTime,
            EndTime = currTime.AddDays(7),
        };

        await auctionRepository.CreateAuctionAsync(auction);
        var response = await auctionRepository.GetAuctionAsync(auction.AuctionId);
        return response == null
            ? Result<AuctionResponseDto>.Failure("Auction not found", FailureReason.NotFound)
            : Result<AuctionResponseDto>.Success(response.ToDto(DateTimeOffset.UtcNow));
    }
}
