namespace Miseria.Core;

public sealed record AccountSnapshot(string AccountName, ulong SteamId, string? IpCountry, bool FamilyViewLocked);
