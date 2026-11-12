namespace Miseria.Core;

public sealed record ProfileSnapshot(
    ulong SteamId,
    string? Name,
    string? AvatarHash,
    FriendPresence? Presence,
    string? GameName,
    string? RealName,
    string? Country,
    string? Summary,
    DateTimeOffset? MemberSince);
