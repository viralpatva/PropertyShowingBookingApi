namespace ShowingBooking.Domain;

/// <summary>
/// A showing time offered by an owner. Owns the seat math: BookedSeats can never exceed Capacity.
/// </summary>
public sealed class Slot
{
    public const int MinDurationMinutes = 15;
    public const int MaxDurationMinutes = 120;
    public const int MinCapacity = 1;
    public const int MaxCapacity = 20;

    private readonly List<Booking> _bookings = new();

    private Slot() { } // for EF Core

    public Guid Id { get; private set; }
    public string PropertyId { get; private set; } = string.Empty;
    public DateTimeOffset StartsAt { get; private set; }
    public int DurationMinutes { get; private set; }
    public int Capacity { get; private set; }

    /// <summary>Seats used by Confirmed, Attended and NoShow bookings. Cancelled bookings free their seats.</summary>
    public int BookedSeats { get; private set; }

    /// <summary>
    /// Concurrency token. Bumped on every change to the seat count, so two requests that
    /// read the same version cannot both save (the second one gets a concurrency conflict).
    /// </summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<Booking> Bookings => _bookings;

    public int SeatsRemaining => Capacity - BookedSeats;

    public static Result<Slot> Create(
        string propertyId,
        DateTimeOffset startsAt,
        int durationMinutes,
        int capacity,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(propertyId))
            return Errors.Validation("propertyId is required.");
        if (startsAt <= now)
            return Errors.Validation("startsAt must be in the future.");
        if (durationMinutes is < MinDurationMinutes or > MaxDurationMinutes)
            return Errors.Validation($"durationMinutes must be between {MinDurationMinutes} and {MaxDurationMinutes}.");
        if (capacity is < MinCapacity or > MaxCapacity)
            return Errors.Validation($"capacity must be between {MinCapacity} and {MaxCapacity}.");

        return new Slot
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId.Trim(),
            StartsAt = startsAt.ToUniversalTime(),
            DurationMinutes = durationMinutes,
            Capacity = capacity
        };
    }

    /// <summary>Books seats for a party. The booking starts as Confirmed.</summary>
    public Result<Booking> Book(string renterEmail, int partySize, DateTimeOffset now)
    {
        var email = Booking.NormalizeEmail(renterEmail);
        if (email.Length == 0)
            return Errors.Validation("renterEmail is required.");
        if (partySize is < Booking.MinPartySize or > Booking.MaxPartySize)
            return Errors.Validation($"partySize must be between {Booking.MinPartySize} and {Booking.MaxPartySize}.");

        if (now >= StartsAt)
            return Errors.SlotStarted;

        // "Active" means Confirmed: a renter who cancelled may book the same slot again.
        if (_bookings.Any(b => b.Status == BookingStatus.Confirmed && b.RenterEmail == email))
            return Errors.DuplicateActiveBooking;

        if (partySize > SeatsRemaining)
            return Errors.NotEnoughSeats(partySize, SeatsRemaining);

        var booking = Booking.Create(this, email, partySize, now);
        _bookings.Add(booking);
        BookedSeats += partySize;
        Version++;
        return booking;
    }

    /// <summary>Called by Booking when it is cancelled.</summary>
    internal void ReleaseSeats(int seats)
    {
        BookedSeats -= seats;
        Version++;
    }
}
