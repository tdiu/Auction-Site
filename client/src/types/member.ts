export type Member = {
  id: string;
  email: string;
  displayName: string;
  imageUrl?: string;
  // Optional: federated accounts never collect one (F7). No template reads it today.
  dateOfBirth?: string;
  createdAt: string;
  lastActive: string;
  description?: string;
}
