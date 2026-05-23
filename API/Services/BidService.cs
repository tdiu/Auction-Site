using API.Core;
using API.Data;
using API.DTOs;
using API.Entities;
using API.Extensions;
using API.Interfaces;
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
        if (auction == null) return Result<BidResponseDto>.Failure("Auction not found");

        if (auction.Status != AuctionStatus.Active || auction.EndTime < DateTimeOffset.UtcNow)
            return Result<BidResponseDto>.Failure("This auction has already ended");

        if (auction.CurrentHighBidderId == userId) 
            return Result<BidResponseDto>.Failure("You are already the highest bidder");

        var amount = bidRequestDto.Amount;
        if (amount <= (auction.CurrentHighBid ?? auction.StartingPrice)) 
            return Result<BidResponseDto>.Failure("Bid is too low");
        
        var currTime = DateTimeOffset.UtcNow;
        
        //
        if (auction.BuyNowPrice.HasValue && amount >= auction.BuyNowPrice)
        {
            auction.Status = AuctionStatus.Ended;
            amount =  auction.BuyNowPrice.Value;
            auction.Status = AuctionStatus.Ended;
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
        
        var success= await unitOfWork.CompleteAsync();
        if (!success) return Result<BidResponseDto>.Failure("Failed to save bid");

        // Populate the bidder to avoid NullRef in ToDto
        newBid.Bidder = (await unitOfWork.Users.GetUserByIdAsync(userId))!;
        
        return Result<BidResponseDto>.Success(newBid.ToDto());
    }
}