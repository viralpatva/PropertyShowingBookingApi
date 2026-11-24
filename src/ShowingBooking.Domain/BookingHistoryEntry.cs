namespace ShowingBooking.Domain;

/// <summary>One row of the audit trail: a single state change of a booking.</summary>
public sealed class BookingHistoryEntry
{
    private BookingHistoryEntry() { } // for EF Core

    // Database-generated, increasing. Gives a stable order even when timestamps are equal.
    public long Id { get; private set; }
    public Guid BookingId { get; private set; }

    /// <summary>Null for the first entry (the booking was just created).</summary>
    public BookingStatus? FromStatus { get; private set; }
    public BookingStatus ToStatus { get; private set; }
    public Actor Actor { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    internal static BookingHistoryEntry Create(
        Guid bookingId,
        BookingStatus? fromStatus,
        BookingStatus toStatus,
        Actor actor,
        string? reason,
        DateTimeOffset occurredAt) => new()
    {
        BookingId = bookingId,
        FromStatus = fromStatus,
        ToStatus = toStatus,
        Actor = actor,
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
        OccurredAt = occurredAt
    };
}
