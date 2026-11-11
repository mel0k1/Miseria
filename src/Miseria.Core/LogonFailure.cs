namespace Miseria.Core;

public enum LogonFailureReason
{
    BadCredentials,
    BadGuardCode,
    RateLimited,
    ConnectionFailed,
    Timeout,
    Cancelled,
    Other
}

public sealed record LogonFailure(LogonFailureReason Reason, string Detail);
