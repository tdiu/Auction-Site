namespace API.Core;

public enum FailureReason
{
    Validation,
    NotFound,
    Unauthorized,
    Conflict,
    InternalError,
    Forbidden
}
