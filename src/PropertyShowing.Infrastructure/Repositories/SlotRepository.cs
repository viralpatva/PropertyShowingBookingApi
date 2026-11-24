namespace PropertyShowing.Infrastructure.Repositories;

public sealed class SlotRepository(BookingDbContext dbContext) : ISlotRepository
{
    public Task<ShowingSlot?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Slots.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task AddAsync(ShowingSlot slot, CancellationToken cancellationToken)
    {
        await dbContext.Slots.AddAsync(slot, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResponse<SlotResponse>> GetPageAsync(
        SlotQueryRequest request,
        CancellationToken cancellationToken)
    {
        // SQLite cannot translate ordering/comparison operations on DateTimeOffset.
        // Materialize the property slots, then apply the date filters and ordering in .NET.
        var propertySlots = await dbContext.Slots
            .Where(x => x.PropertyId == request.PropertyId)
            .ToListAsync(cancellationToken);

        var filteredSlots = propertySlots
            .Where(x => !request.From.HasValue || x.StartsAt >= request.From.Value)
            .Where(x => !request.To.HasValue || x.StartsAt <= request.To.Value)
            .OrderBy(x => x.StartsAt)
            .ToArray();

        var totalCount = filteredSlots.Length;
        var skip = ((long)request.Page - 1) * request.PageSize;

        // A valid page number can still produce an offset larger than Int32.
        // Return an empty page rather than overflowing the Skip calculation.
        var pageSlots = skip >= totalCount
            ? Array.Empty<ShowingSlot>()
            : filteredSlots.Skip((int)skip).Take(request.PageSize);

        var items = pageSlots
            .Select(x => new SlotResponse(
                x.Id,
                x.PropertyId,
                x.StartsAt,
                x.DurationMinutes,
                x.Capacity,
                x.BookedSeats,
                x.Capacity - x.BookedSeats))
            .ToArray();

        return new PagedResponse<SlotResponse>(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<bool> TryReserveSeatsAsync(Guid slotId, int partySize, CancellationToken cancellationToken)
    {
        // A single conditional UPDATE is atomic in SQLite and prevents two requests taking the same last seats.
        var affected = await dbContext.Slots
            .Where(x => x.Id == slotId && x.BookedSeats + partySize <= x.Capacity)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.BookedSeats, x => x.BookedSeats + partySize), cancellationToken);
        return affected == 1;
    }

    public async Task ReleaseSeatsAsync(Guid slotId, int partySize, CancellationToken cancellationToken)
    {
        await dbContext.Slots
            .Where(x => x.Id == slotId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.BookedSeats, x => x.BookedSeats - partySize), cancellationToken);
    }
}
