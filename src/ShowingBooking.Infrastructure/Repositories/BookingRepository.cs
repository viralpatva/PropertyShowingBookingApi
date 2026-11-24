using Microsoft.EntityFrameworkCore;
using ShowingBooking.Application.Abstractions;
using ShowingBooking.Domain;

namespace ShowingBooking.Infrastructure.Repositories;

internal sealed class BookingRepository(ShowingDbContext db) : IBookingRepository
{
    public Task<Booking?> GetWithSlotAsync(Guid id, CancellationToken cancellationToken) =>
        db.Bookings
            .Include(b => b.Slot)
            .SingleOrDefaultAsync(b => b.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Bookings.AsNoTracking().AnyAsync(b => b.Id == id, cancellationToken);

    public async Task<IReadOnlyList<BookingHistoryEntry>> GetHistoryAsync(
        Guid bookingId, CancellationToken cancellationToken) =>
        await db.History
            .AsNoTracking()
            .Where(h => h.BookingId == bookingId)
            .OrderBy(h => h.Id) // database-generated and increasing = order in which changes happened
            .ToListAsync(cancellationToken);
}
