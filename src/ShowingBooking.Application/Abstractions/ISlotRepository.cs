using ShowingBooking.Domain;

namespace ShowingBooking.Application.Abstractions;

public interface ISlotRepository
{
    /// <summary>Loads a slot together with its Confirmed bookings (needed for the duplicate check).</summary>
    Task<Slot?> GetWithConfirmedBookingsAsync(Guid id, CancellationToken cancellationToken);

    void Add(Slot slot);

    /// <summary>
    /// Filtered, paged list ordered by start time. from is inclusive, to is exclusive.
    /// Returned entities are read-only snapshots (not tracked).
    /// </summary>
    Task<(IReadOnlyList<Slot> Items, int TotalCount)> ListAsync(
        string? propertyId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
