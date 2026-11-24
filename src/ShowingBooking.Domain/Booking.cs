namespace ShowingBooking.Domain;

/// <summary>
/// A renter's reservation of seats on a slot. Owns the state machine:
///   Confirmed -> Cancelled (renter or owner, strictly before the slot starts, frees seats)
///   Confirmed -> Attended | NoShow (owner only, at or after the slot starts)
///   Cancelled, Attended, NoShow are final.
/// </summary>
public sealed class Booking
{
    public const int MinPartySize = 1;
    public const int MaxPartySize = 6;

    private readonly List<BookingHistoryEntry> _history = new();

    private Booking() { } // for EF Core

    public Guid Id { get; private set; }
    public Guid SlotId { get; private set; }
    public Slot Slot { get; private set; } = null!;
    public string RenterEmail { get; private set; } = string.Empty;
    public int PartySize { get; private set; }
    public BookingStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Concurrency token, bumped on every state change.</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<BookingHistoryEntry> History => _history;

    public static string NormalizeEmail(string? email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();

    // Only Slot.Book creates bookings, so the seat math cannot be bypassed.
    internal static Booking Create(Slot slot, string normalizedEmail, int partySize, DateTimeOffset now)
    {
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            SlotId = slot.Id,
            Slot = slot,
            RenterEmail = normalizedEmail,
            PartySize = partySize,
            Status = BookingStatus.Confirmed,
            CreatedAt = now
        };

        booking._history.Add(BookingHistoryEntry.Create(
            booking.Id, null, BookingStatus.Confirmed, Actor.Renter, "Booking created", now));

        return booking;
    }

    public Result Transition(BookingStatus to, Actor actor, string? reason, DateTimeOffset now)
    {
        // Cancelled, Attended and NoShow are final.
        if (Status != BookingStatus.Confirmed)
            return Errors.InvalidTransition(Status, to);

        switch (to)
        {
            case BookingStatus.Cancelled:
                // Renter or owner may cancel, but only strictly before the slot starts.
                if (now >= Slot.StartsAt)
                    return Errors.TooLateToCancel;
                Slot.ReleaseSeats(PartySize);
                break;

            case BookingStatus.Attended:
            case BookingStatus.NoShow:
                if (actor != Actor.Owner)
                    return Errors.ActorNotAllowed;
                if (now < Slot.StartsAt)
                    return Errors.TooEarlyToComplete;
                break;

            default: // Confirmed -> Confirmed is not a transition
                return Errors.InvalidTransition(Status, to);
        }

        var previous = Status;
        Status = to;
        Version++;
        _history.Add(BookingHistoryEntry.Create(Id, previous, to, actor, reason, now));
        return Result.Success();
    }
}
