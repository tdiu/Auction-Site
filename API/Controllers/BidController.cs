using API.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;


public class BidController (IAuctionService auctionService) : BaseApiController
{
   [HttpGet("api/auctions/{auctionId}/bids")]
   public async Task<IActionResult> GetBids(int auctionId)
   {
      return Ok();
   }
   
}