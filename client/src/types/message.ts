export type Message = {
  id: string
  senderId: string
  senderDisplayName: string
  recipientId: string
  recipientDisplayName: string
  content: string
  dateRead?: string
  messageSent: string
}
