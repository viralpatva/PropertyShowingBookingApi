namespace PropertyShowing.Application.Abstractions;

public interface ISlotService
{
    Task<(SlotResponse? Slot, string? Error)> CreateSlotAsync(
        CreateSlotRequest request,
        CancellationToken cancellationToken);

    Task<PagedResponse<SlotResponse>> GetSlotsAsync(
        SlotQueryRequest request,
        CancellationToken cancellationToken);
}
