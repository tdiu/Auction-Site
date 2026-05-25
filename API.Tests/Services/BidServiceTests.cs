using API.Core;
using API.DTOs;
using API.Entities;
using API.Interfaces;
using API.Services;
using NSubstitute;
using Xunit;

namespace API.Tests.Services;

public class BidServiceTests
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBidRepository _bidRepo;
    private readonly IAuctionRepository _auctionRepo;
    private readonly IUserRepository _userRepo;
    private readonly BidService _sut;

    public BidServiceTests()
    {
        _bidRepo = Substitute.For<IBidRepository>();
        _auctionRepo = Substitute.For<IAuctionRepository>();
        _userRepo = Substitute.For<IUserRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();

        _unitOfWork.Bids.Returns(_bidRepo);
        _unitOfWork.Auctions.Returns(_auctionRepo);
        _unitOfWork.Users.Returns(_userRepo);

        _sut = new BidService(_unitOfWork);
    }

    [Fact]
    public async Task PlaceBid_WithValidBid_ReturnsSuccessAndCreatesBid()
    {
        // Arrange
        var auction = new Auction
        {
            AuctionId = 1,
            Status = AuctionStatus.Active,
            StartingPrice = 100m,
            CurrentHighBid = null,
            CurrentHighBidderId = null,
            EndTime = DateTimeOffset.UtcNow.AddDays(1),
            SellerId = "seller-guid",
            ItemName = "Test Item",
            StartTime = DateTimeOffset.UtcNow,
        };
        _auctionRepo.GetAuctionAsync(1).Returns(auction);
        _unitOfWork.CompleteAsync().Returns(true);

        var bidder = new AppUser
        {
            Id = "bidder-guid",
            DisplayName = "TestBidder",
            Email = "bidder@test.com",
        };
        _userRepo.GetUserByIdAsync("bidder-guid").Returns(bidder);

        var dto = new BidRequestDto { Amount = 150m };

        // Act
        var result = await _sut.PlaceBid(dto, 1, "bidder-guid");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(150m, result.Value!.BidAmount);
        Assert.Equal(150m, auction.CurrentHighBid);
        Assert.Equal("bidder-guid", auction.CurrentHighBidderId);

        _bidRepo.Received(1).Add(Arg.Is<Bid>(b =>
            b.AuctionId == 1 &&
            b.BidAmount == 150m &&
            b.BidderId == "bidder-guid"));
    }

    [Fact]
    public async Task PlaceBid_WhenAuctionExpired_ReturnsFailure()
    {
        var auction = new Auction
        {
            AuctionId = 1,
            Status = AuctionStatus.Active,
            StartingPrice = 100m,
            EndTime = DateTimeOffset.UtcNow.AddHours(-1),
            SellerId = "seller-guid",
            ItemName = "Test",
            StartTime = DateTimeOffset.UtcNow.AddDays(-8),
        };
        _auctionRepo.GetAuctionAsync(1).Returns(auction);

        var result = await _sut.PlaceBid(new BidRequestDto { Amount = 150m }, 1, "bidder-guid");

        Assert.False(result.IsSuccess);
        Assert.Equal("This auction has already ended", result.Error);
    }

    [Fact]
    public async Task PlaceBid_WhenUserIsHighestBidder_ReturnsFailure()
    {
        var auction = new Auction
        {
            AuctionId = 1,
            Status = AuctionStatus.Active,
            StartingPrice = 100m,
            CurrentHighBid = 150m,
            CurrentHighBidderId = "bidder-guid",
            EndTime = DateTimeOffset.UtcNow.AddDays(1),
            SellerId = "seller-guid",
            ItemName = "Test",
            StartTime = DateTimeOffset.UtcNow,
        };
        _auctionRepo.GetAuctionAsync(1).Returns(auction);

        var result = await _sut.PlaceBid(new BidRequestDto { Amount = 200m }, 1, "bidder-guid");

        Assert.False(result.IsSuccess);
        Assert.Equal("You are already the highest bidder", result.Error);
    }

    [Fact]
    public async Task PlaceBid_WithAmountBelowOrEqualToStartingPrice_ReturnsFailure()
    {
        var auction = new Auction
        {
            AuctionId = 1,
            Status = AuctionStatus.Active,
            StartingPrice = 100m,
            CurrentHighBid = null,
            CurrentHighBidderId = null,
            EndTime = DateTimeOffset.UtcNow.AddDays(1),
            SellerId = "seller-guid",
            ItemName = "Test",
            StartTime = DateTimeOffset.UtcNow,
        };
        _auctionRepo.GetAuctionAsync(1).Returns(auction);

        var result = await _sut.PlaceBid(new BidRequestDto { Amount = 50m }, 1, "bidder-guid");

        Assert.False(result.IsSuccess);
        Assert.Equal("Bid is too low", result.Error);
    }

    [Fact]
    public async Task PlaceBid_WithAmountBelowCurrentHighBid_ReturnsFailure()
    {
        var auction = new Auction
        {
            AuctionId = 1,
            Status = AuctionStatus.Active,
            StartingPrice = 100m,
            CurrentHighBid = 150m,
            CurrentHighBidderId = "other-bidder",
            EndTime = DateTimeOffset.UtcNow.AddDays(1),
            SellerId = "seller-guid",
            ItemName = "Test",
            StartTime = DateTimeOffset.UtcNow,
        };
        _auctionRepo.GetAuctionAsync(1).Returns(auction);

        var result = await _sut.PlaceBid(new BidRequestDto { Amount = 120m }, 1, "bidder-guid");

        Assert.False(result.IsSuccess);
        Assert.Equal("Bid is too low", result.Error);
    }

    [Fact]
    public async Task PlaceBid_BuyNowPrice_EndsAuctionAndClampsAmount()
    {
        var auction = new Auction
        {
            AuctionId = 1,
            Status = AuctionStatus.Active,
            StartingPrice = 100m,
            BuyNowPrice = 500m,
            CurrentHighBid = null,
            CurrentHighBidderId = null,
            EndTime = DateTimeOffset.UtcNow.AddDays(1),
            SellerId = "seller-guid",
            ItemName = "Test",
            StartTime = DateTimeOffset.UtcNow,
        };
        _auctionRepo.GetAuctionAsync(1).Returns(auction);
        _unitOfWork.CompleteAsync().Returns(true);

        var bidder = new AppUser { Id = "bidder-guid", DisplayName = "Bidder" };
        _userRepo.GetUserByIdAsync("bidder-guid").Returns(bidder);

        var result = await _sut.PlaceBid(new BidRequestDto { Amount = 1000m }, 1, "bidder-guid");

        Assert.True(result.IsSuccess);
        Assert.Equal(500m, result.Value!.BidAmount);
        Assert.Equal(AuctionStatus.Ended, auction.Status);
    }

    [Fact]
    public async Task PlaceBid_WhenAuctionNotFound_ReturnsFailure()
    {
        _auctionRepo.GetAuctionAsync(999).Returns((Auction?)null);

        var result = await _sut.PlaceBid(new BidRequestDto { Amount = 100m }, 999, "bidder-guid");

        Assert.False(result.IsSuccess);
        Assert.Equal("Auction not found", result.Error);
    }
}
