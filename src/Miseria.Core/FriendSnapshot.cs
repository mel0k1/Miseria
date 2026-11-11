namespace Miseria.Core;

public enum FriendPresence
{
    Offline,
    Online,
    Busy,
    Away,
    Snooze,
    LookingToTrade,
    LookingToPlay,
    InGame
}

public sealed record FriendSnapshot(ulong SteamId, string Name, FriendPresence Presence, string? AvatarHash, string? GameName);
