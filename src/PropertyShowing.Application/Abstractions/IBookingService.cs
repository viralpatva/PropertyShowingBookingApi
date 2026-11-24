namespace PropertyShowing.Application.Abstractions;

public interface IBookingService
{
    Task<(BookingResponse? Booking, string? Error, int StatusCode)> CreateBookingAsync(
        Guid slotId,
        CreateBookingRequest request,
        CancellationToken cancellationToken);

    Task<(bool Success, string? Error, int StatusCode)> TransitionAsync(
        Guid bookingId,
        TransitionBookingRequest request,
        CancellationToken cancellationToken);

    Task<(IReadOnlyCollection<BookingHistoryResponse>? History, string? Error)> GetHistoryAsync(
        Guid bookingId,
        CancellationToken cancellationToken);
}
