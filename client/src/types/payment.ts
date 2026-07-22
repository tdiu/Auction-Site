// Mirrors the server's effective status. 'Failed' and 'Expired' are folded up from the latest
// PaymentAttempt; Payment.Status itself only knows Pending | Paid.
export type PaymentStatus = 'Pending' | 'Paid' | 'Failed' | 'Expired';

export type PaymentStatusDto = {
  status: PaymentStatus,
  amount: number,
  completedAt?: string,
}

export type CreatePaymentResponse = {
  checkoutUrl: string,
}
