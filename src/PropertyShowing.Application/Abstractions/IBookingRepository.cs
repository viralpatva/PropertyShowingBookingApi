namespace PropertyShowing.Application.Abstractions;

public interface IBookingRepository
{
    Task<Booking?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> HasActiveBookingAsync(Guid slotId, string renterEmail, CancellationToken cancellationToken);
    Task AddAsync(Booking booking, CancellationToken cancellationToken);
    Task AddHistoryAsync(BookingHistory history, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task<bool> BookingExistsAsync(Guid bookingId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BookingHistoryResponse>> GetHistoryAsync(Guid bookingId, CancellationToken cancellationToken);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken);
}
