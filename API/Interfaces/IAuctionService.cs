using API.Core;
using API.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace API.Interfaces;

public interface IAuctionService
{
    Task<IReadOnlyList<AuctionResponseDto>> GetAllAuctions(string? displayName, string? searchTerm);
    Task<Result<AuctionResponseDto>> GetAuctionById(int id);
    Task<Result<AuctionResponseDto>> CreateAuction(AuctionRequestDto auctionRequestDto, string userId);
    
}