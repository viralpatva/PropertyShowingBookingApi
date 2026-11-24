using ShowingBooking.Domain;

namespace ShowingBooking.Application.Abstractions;

public interface IBookingRepository
{
    /// <summary>Loads a booking with its slot (cancelling gives seats back to the slot).</summary>
    Task<Booking?> GetWithSlotAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Every state change of the booking, in the order it happened.</summary>
    Task<IReadOnlyList<BookingHistoryEntry>> GetHistoryAsync(Guid bookingId, CancellationToken cancellationToken);
}
