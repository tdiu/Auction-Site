using API.Core;
using API.DTOs;
using API.Entities;
using API.Interfaces;
using API.Services;
using NSubstitute;
using Xunit;

namespace API.Tests.Services;

public class AuctionServiceTests
{
    private readonly IAuctionRepository _auctionRepository;
    private readonly AuctionService _sut;

    public AuctionServiceTests()
    {
        _auctionRepository = Substitute.For<IAuctionRepository>();
        _sut = new AuctionService(_auctionRepository);
    }

    [Fact]
    public async Task CreateAuction_TrimsItemNameBeforePersisting()
    {
        Auction? savedAuction = null;
        var seller = new AppUser
        {
            Id = "seller-guid",
            DisplayName = "Seller",
            Email = "seller@test.com",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        _auctionRepository.CreateAuctionAsync(Arg.Do<Auction>(auction =>
        {
            auction.AuctionId = 10;
            auction.Seller = seller;
            savedAuction = auction;
        })).Returns(call => Task.FromResult(call.Arg<Auction>()));

        _auctionRepository.GetAuctionAsync(10)
            .Returns(_ => Task.FromResult<Auction?>(savedAuction));

        var result = await _sut.CreateAuction(new AuctionRequestDto
        {
            ItemName = "  Vintage Guitar  ",
            StartingPrice = 100m
        }, "seller-guid");

        Assert.True(result.IsSuccess);
        Assert.Equal("Vintage Guitar", result.Value!.ItemName);

        await _auctionRepository.Received(1).CreateAuctionAsync(Arg.Is<Auction>(auction =>
            auction.ItemName == "Vintage Guitar"));
    }

    [Fact]
    public async Task CreateAuction_WithWhitespaceItemName_ReturnsFailure()
    {
        var result = await _sut.CreateAuction(new AuctionRequestDto
        {
            ItemName = "   ",
            StartingPrice = 100m
        }, "seller-guid");

        Assert.False(result.IsSuccess);
        Assert.Equal("Item name is required", result.Error);
        Assert.NotNull(result.ValidationErrors);
        Assert.Contains("Item name is required", result.ValidationErrors["itemName"]);
        Assert.Equal(FailureReason.Validation, result.Reason);

        await _auctionRepository.DidNotReceive().CreateAuctionAsync(Arg.Any<Auction>());
    }
}
