namespace ShowingBooking.Application.Abstractions;

/// <summary>
/// Saves everything that was changed through the repositories in one atomic step.
/// Implemented in Infrastructure, so the application layer knows nothing about EF Core.
/// </summary>
public interface IUnitOfWork
{
    /// <exception cref="ConcurrencyConflictException">
    /// Another request changed the same data first (or the database was busy).
    /// </exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Forgets every entity loaded so far, so a retry starts from fresh data.</summary>
    void ClearTracking();
}

/// <summary>
/// Thrown by the persistence layer when a save lost a race with another request.
/// Defined here so the application layer does not need to know about SQLite or EF Core.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message, Exception? inner = null) : base(message, inner) { }
}
