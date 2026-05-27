using API.Core;
using API.DTOs;
using API.Entities;
using Microsoft.AspNetCore.Mvc;

namespace API.Interfaces;

public interface IAuctionService
{
    Task<PagedList<AuctionResponseDto>> GetAllAuctions(
        string? displayName,
        string? searchTerm,
        AuctionStatus? status,
        int page,
        int pageSize
        );
    Task<Result<AuctionResponseDto>> GetAuctionById(int id);
    Task<Result<AuctionResponseDto>> CreateAuction(AuctionRequestDto auctionRequestDto, string userId);

}
