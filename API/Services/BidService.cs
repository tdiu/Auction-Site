using API.Core;
using API.Data;
using API.DTOs;
using API.Entities;
using API.Extensions;
using API.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API.Services;

public class BidService(IUnitOfWork unitOfWork) : IBidService
{
    public async Task<IReadOnlyList<BidResponseDto>> GetAllBids(int auctionId)
    {
        var query = unitOfWork.Bids.GetBidsQueryable()
            .Where(b => b.AuctionId == auctionId)
            .OrderByDescending(b => b.BidDate);

        var bids = await query.ProjectToDto().ToListAsync();
        bids.ObfuscateBidderNames();
        return bids;
    }

    public async Task<Result<BidResponseDto>> PlaceBid(BidRequestDto bidRequestDto, int auctionId, string userId)
    {
        var auction = await unitOfWork.Auctions.GetAuctionAsync(auctionId);
        var amount = bidRequestDto.Amount;

        if (auction == null)
            return Result<BidResponseDto>.Failure("Auction not found", FailureReason.NotFound);
        if (auction.SellerId == userId)
            return Result<BidResponseDto>.Failure("You cannot bid on your own auction", FailureReason.Validation);
        if (auction.EndTime < DateTimeOffset.UtcNow)
            return Result<BidResponseDto>.Failure("This auction has already ended", FailureReason.Validation);
        if (auction.CurrentHighBidderId == userId)
            return Result<BidResponseDto>.Failure("You are already the highest bidder", FailureReason.Conflict);
        if (amount <= (auction.CurrentHighBid ?? auction.StartingPrice))
            return Result<BidResponseDto>.Failure("Bid is too low", FailureReason.Validation);

        var currTime = DateTimeOffset.UtcNow;

        if (auction.BuyNowPrice.HasValue && amount >= auction.BuyNowPrice)
        {
            amount = auction.BuyNowPrice.Value;
            auction.EndTime = currTime;
        }

        var newBid = new Bid
        {
            AuctionId = auction.AuctionId,
            BidAmount = amount,
            BidderId = userId,
            BidDate = currTime
        };

        unitOfWork.Bids.Add(newBid);
        auction.CurrentHighBid = amount;
        auction.CurrentHighBidderId = userId;

        try
        {
            var success = await unitOfWork.CompleteAsync();
            if (!success) return Result<BidResponseDto>.Failure("Failed to save bid", FailureReason.InternalError);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<BidResponseDto>.Failure("Auction has been updated. Please refresh and try again", FailureReason.Conflict);
        }

        // Populate the bidder to avoid NullRef in ToDto
        newBid.Bidder = (await unitOfWork.Users.GetUserByIdAsync(userId))!;

        return Result<BidResponseDto>.Success(newBid.ToDto());
    }
}
