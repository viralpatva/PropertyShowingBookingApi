namespace PropertyShowing.Api.Repositories;

public interface ISlotRepository
{
    Task<ShowingSlot?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(ShowingSlot slot, CancellationToken cancellationToken);
    Task<PagedResponse<SlotResponse>> GetPageAsync(SlotQueryRequest request, CancellationToken cancellationToken);
    Task<bool> TryReserveSeatsAsync(Guid slotId, int partySize, CancellationToken cancellationToken);
    Task ReleaseSeatsAsync(Guid slotId, int partySize, CancellationToken cancellationToken);
}

public interface IBookingRepository
{
    Task<Booking?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> HasActiveBookingAsync(Guid slotId, string renterEmail, CancellationToken cancellationToken);
    Task AddAsync(Booking booking, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task<bool> BookingExistsAsync(Guid bookingId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BookingHistoryResponse>> GetHistoryAsync(Guid bookingId, CancellationToken cancellationToken);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken);
}
