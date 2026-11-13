namespace Miseria.Core;

public sealed record AchievementSnapshot(
    string Name,
    string? Description,
    bool Hidden,
    string IconUrl,
    string IconGrayUrl,
    double GlobalPercent,
    bool Unlocked,
    DateTimeOffset? UnlockTime,
    int Progress,
    int MinProgress,
    int MaxProgress,
    string? GroupName);

public sealed record GameAchievementsSnapshot(uint AppId, IReadOnlyList<AchievementSnapshot> Achievements);
