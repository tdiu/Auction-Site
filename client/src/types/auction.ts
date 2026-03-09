export type Auction = {
  auctionId: string,
  itemName: string,
  imageUrl?: string,
  startingPrice: number,
  buyNowPrice: number,
  sellerId: string,
  sellerName: string,
  startTime: string,
  endTime: string,
}
