export type Bid = {
  bidId: string,
  bidAmount: number,
  bidDate: string,
  auctionId: string,
  bidderId: string,
  bidderName: string
}

export type BidRequest = {
  amount: number
}
