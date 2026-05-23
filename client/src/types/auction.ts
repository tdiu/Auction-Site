export type Auction = {
  auctionId: string,
  itemName: string,
  imageUrl?: string,
  startingPrice: number,
  buyNowPrice: number,
  currentHighBid?: number,
  currentHighBidderId?: string,
  sellerId: string,
  sellerName: string,
  startTime: string,
  endTime: string,
  status: string,
}

export type AuctionRequest = {
  itemName: string,
  startingPrice: number,
  buyNowPrice?: number | null;
}
