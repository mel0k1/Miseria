namespace Miseria.Core;

internal sealed class GuardCancelledException : Exception
{
    public GuardCancelledException()
        : base("Steam Guard prompt was cancelled")
    {
    }
}
