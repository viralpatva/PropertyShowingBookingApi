using Microsoft.EntityFrameworkCore;
using ShowingBooking.Application.Abstractions;
using ShowingBooking.Domain;

namespace ShowingBooking.Infrastructure.Repositories;

internal sealed class SlotRepository(ShowingDbContext db) : ISlotRepository
{
    public Task<Slot?> GetWithConfirmedBookingsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Slots
            .Include(s => s.Bookings.Where(b => b.Status == BookingStatus.Confirmed))
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

    public void Add(Slot slot) => db.Slots.Add(slot);

    public async Task<(IReadOnlyList<Slot> Items, int TotalCount)> ListAsync(
        string? propertyId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = db.Slots.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(propertyId))
        {
            var trimmed = propertyId.Trim();
            query = query.Where(s => s.PropertyId == trimmed);
        }

        // from is inclusive, to is exclusive, both compared with the slot's start time.
        if (from is { } fromTime)
            query = query.Where(s => s.StartsAt >= fromTime);
        if (to is { } toTime)
            query = query.Where(s => s.StartsAt < toTime);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(s => s.StartsAt)
            .ThenBy(s => s.Id) // stable order when two slots start at the same time
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
