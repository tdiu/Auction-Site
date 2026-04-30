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
        
        return await query.ProjectToDto().ToListAsync();
    }

    public async Task<Result<BidResponseDto>> PlaceBid(BidRequestDto bidRequestDto, int auctionId, string userId)
    {
        var auction = await unitOfWork.Auctions.GetAuctionAsync(auctionId);
        var amount = bidRequestDto.Amount;
        
        if (auction == null) throw new Exception("Auction not found");

        if (amount <= auction.CurrentHighBid) return Result<BidResponseDto>.Failure("Bid is too low");
        
        var currTime = DateTimeOffset.UtcNow;
        
        var newBid = new Bid
        {
            AuctionId = auction.AuctionId,
            BidAmount = amount,
            BidderId = userId,
            BidDate = currTime
        };

        unitOfWork.Bids.Add(newBid);
        auction.CurrentHighBid = amount;
        
        var success= await unitOfWork.CompleteAsync();
        if (!success) return Result<BidResponseDto>.Failure("Failed to save bid");
        
        return Result<BidResponseDto>.Success(newBid.ToDto());
    }
}