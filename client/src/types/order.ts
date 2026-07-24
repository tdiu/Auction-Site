// Same status vocabulary as PaymentStatus. Both come from ToEffectiveStatus server-side.
import {PaymentStatus} from './payment';

export type Order = {
  orderId: number,
  auctionId: number,
  itemName: string,
  sellerName: string,
  amount: number,
  status: PaymentStatus,
  createdAt: string,
  completedAt?: string,
}
