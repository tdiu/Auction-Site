namespace API.Entities;

public enum SessionRevokedReason
{
    Rotated,
    LoggedOut,
    ReuseDetected,
    PasswordReset
}
