export type User = {
  id: string;
  displayName: string;
  email: string;
  token: string;
  imageUrl?: string;

  // 'password', or a provider name straight out of AspNetUserLogins ('Google', 'Microsoft', ...).
  // Deliberately open: the server reads the login row, so a new provider is a server-side change
  // and a closed union here would turn it into a client change too.
  authProvider: string;
}

// The only comparison the client should make on authProvider. Everything the UI branches on is
// "does this account have a password" (show password controls, offer a reset), never "which
// provider is it" -- that string is for display.
export const isPasswordAccount = (user: User) => user.authProvider === 'password';

export type LoginCreds = {
  email: string;
  password: string;
}

export type RegisterCreds = {
  email: string;
  displayName: string;
  password: string;
  dateOfBirth: string;
}
