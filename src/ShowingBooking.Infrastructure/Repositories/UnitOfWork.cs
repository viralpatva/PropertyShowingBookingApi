using ShowingBooking.Application.Abstractions;

namespace ShowingBooking.Infrastructure.Repositories;

internal sealed class UnitOfWork(ShowingDbContext db) : IUnitOfWork
{
    // ShowingDbContext.SaveChangesAsync turns lost races into ConcurrencyConflictException.
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);

    public void ClearTracking() => db.ChangeTracker.Clear();
}
