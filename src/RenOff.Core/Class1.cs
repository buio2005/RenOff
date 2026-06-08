namespace RenOff.Core;

public enum RenOffItemType
{
    Note = 0,
    Todo = 1,
}

public enum ReminderStatus
{
    Scheduled = 0,
    Snoozed = 1,
    Fired = 2,
    Dismissed = 3,
}

public sealed class RenOffItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int SortOrder { get; set; }
    public RenOffItemType Type { get; set; } = RenOffItemType.Todo;
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsDone { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Reminder
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ItemId { get; init; }
    public DateTimeOffset ScheduledAtUtc { get; set; }
    public DateTimeOffset? SnoozedUntilUtc { get; set; }
    public ReminderStatus Status { get; set; } = ReminderStatus.Scheduled;
    public DateTimeOffset? LastFiredAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset EffectiveAtUtc => SnoozedUntilUtc ?? ScheduledAtUtc;
}

public sealed class ReminderNotification
{
    public Guid ReminderId { get; init; }
    public Guid ItemId { get; init; }
    public string ItemTitle { get; init; } = "";
    public DateTimeOffset EffectiveAtUtc { get; init; }
}
