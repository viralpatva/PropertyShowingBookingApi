namespace PropertyShowing.Application.Services;

public sealed class SlotService(ISlotRepository slotRepository) : ISlotService
{
    public async Task<(SlotResponse? Slot, string? Error)> CreateSlotAsync(
        CreateSlotRequest request,
        CancellationToken cancellationToken)
    {
        var slot = new ShowingSlot
        {
            Id = Guid.NewGuid(),
            PropertyId = request.PropertyId,
            StartsAt = request.StartsAt,
            DurationMinutes = request.DurationMinutes,
            Capacity = request.Capacity,
            BookedSeats = 0
        };

        await slotRepository.AddAsync(slot, cancellationToken);

        return (
            new SlotResponse(
                slot.Id,
                slot.PropertyId,
                slot.StartsAt,
                slot.DurationMinutes,
                slot.Capacity,
                0,
                slot.Capacity),
            null);
    }

    public Task<PagedResponse<SlotResponse>> GetSlotsAsync(
        SlotQueryRequest request,
        CancellationToken cancellationToken) =>
        slotRepository.GetPageAsync(request, cancellationToken);
}
