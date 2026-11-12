namespace Miseria.Core;

public sealed record ChatMessage(ulong PeerId, ulong AuthorId, string Text, DateTimeOffset Timestamp);
