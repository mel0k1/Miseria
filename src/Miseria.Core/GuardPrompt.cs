namespace Miseria.Core;

public enum GuardKind
{
    EmailCode,
    DeviceCode
}

public sealed record GuardPrompt(GuardKind Kind, string? Email, bool PreviousCodeWasIncorrect);
