namespace PropertyShowing.Api.Repositories;

public sealed class SlotRepository(BookingDbContext dbContext) : ISlotRepository
{
    public Task<ShowingSlot?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Slots.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task AddAsync(ShowingSlot slot, CancellationToken cancellationToken)
    {
        await dbContext.Slots.AddAsync(slot, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResponse<SlotResponse>> GetPageAsync(SlotQueryRequest request, CancellationToken cancellationToken)
    {
        var query = dbContext.Slots.Where(x => x.PropertyId == request.PropertyId);
        if (request.From.HasValue) query = query.Where(x => x.StartsAt >= request.From.Value);
        if (request.To.HasValue) query = query.Where(x => x.StartsAt <= request.To.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.StartsAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new SlotResponse(x.Id, x.PropertyId, x.StartsAt, x.DurationMinutes, x.Capacity, x.BookedSeats, x.Capacity - x.BookedSeats))
            .ToListAsync(cancellationToken);
        return new PagedResponse<SlotResponse>(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<bool> TryReserveSeatsAsync(Guid slotId, int partySize, CancellationToken cancellationToken)
    {
        var affected = await dbContext.Slots
            .Where(x => x.Id == slotId && x.BookedSeats + partySize <= x.Capacity)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.BookedSeats, x => x.BookedSeats + partySize), cancellationToken);
        return affected == 1;
    }

    public async Task ReleaseSeatsAsync(Guid slotId, int partySize, CancellationToken cancellationToken)
    {
        await dbContext.Slots
            .Where(x => x.Id == slotId && x.BookedSeats >= partySize)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.BookedSeats, x => x.BookedSeats - partySize), cancellationToken);
    }
}
