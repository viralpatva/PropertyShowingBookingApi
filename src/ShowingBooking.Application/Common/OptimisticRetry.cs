using ShowingBooking.Application.Abstractions;
using ShowingBooking.Domain;

namespace ShowingBooking.Application.Common;

/// <summary>
/// Runs a "load, apply business rule, save" operation and repeats it from fresh data when the
/// save loses a race. Retrying is safe because the whole operation is re-run, including the
/// seat check, so a retry either succeeds or fails with not_enough_seats. It can never overbook.
/// </summary>
internal static class OptimisticRetry
{
    private const int MaxAttempts = 5;

    public static async Task<Result<T>> RunAsync<T>(IUnitOfWork unitOfWork, Func<Task<Result<T>>> operation)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (ConcurrencyConflictException)
            {
                // Someone else changed the slot first. Forget everything we loaded and start over.
                unitOfWork.ClearTracking();

                if (attempt < MaxAttempts)
                    await Task.Delay(Random.Shared.Next(5, 30)); // small random pause so racers spread out
            }
        }

        return Errors.SlotBusy;
    }
}
