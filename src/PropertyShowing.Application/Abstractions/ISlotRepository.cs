namespace PropertyShowing.Application.Abstractions;

public interface ISlotRepository
{
    Task<ShowingSlot?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(ShowingSlot slot, CancellationToken cancellationToken);
    Task<PagedResponse<SlotResponse>> GetPageAsync(SlotQueryRequest request, CancellationToken cancellationToken);
    Task<bool> TryReserveSeatsAsync(Guid slotId, int partySize, CancellationToken cancellationToken);
    Task ReleaseSeatsAsync(Guid slotId, int partySize, CancellationToken cancellationToken);
}
