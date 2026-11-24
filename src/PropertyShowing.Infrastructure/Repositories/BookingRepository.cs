namespace PropertyShowing.Infrastructure.Repositories;

public sealed class BookingRepository(BookingDbContext dbContext) : IBookingRepository
{
    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await action(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<Booking?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Bookings.AsTracking().Include(x => x.Slot).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<bool> HasActiveBookingAsync(Guid slotId, string renterEmail, CancellationToken cancellationToken) =>
        dbContext.Bookings.AnyAsync(x => x.SlotId == slotId && x.RenterEmail == renterEmail && x.Status == BookingStatus.Confirmed, cancellationToken);

    public async Task AddAsync(Booking booking, CancellationToken cancellationToken)
    {
        await dbContext.Bookings.AddAsync(booking, cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            // The filtered unique index protects against concurrent duplicate active bookings.
            throw new DuplicateActiveBookingException();
        }
    }

    public async Task AddHistoryAsync(BookingHistory history, CancellationToken cancellationToken)
    {
        await dbContext.BookingHistories.AddAsync(history, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new BookingConcurrencyException(exception);
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 };

    public Task<bool> BookingExistsAsync(Guid bookingId, CancellationToken cancellationToken) =>
        dbContext.Bookings.AnyAsync(x => x.Id == bookingId, cancellationToken);

    public async Task<IReadOnlyCollection<BookingHistoryResponse>> GetHistoryAsync(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        var historyEntries = await dbContext.BookingHistories
            .Where(x => x.BookingId == bookingId)
            .ToListAsync(cancellationToken);

        return historyEntries
            .OrderBy(x => x.ChangedAt)
            .Select(x => new BookingHistoryResponse(
                x.FromStatus.ToString(),
                x.ToStatus.ToString(),
                x.ChangedAt,
                x.Actor,
                x.Reason))
            .ToArray();
    }
}
