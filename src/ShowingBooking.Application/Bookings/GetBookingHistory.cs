using MediatR;
using ShowingBooking.Application.Abstractions;
using ShowingBooking.Domain;

namespace ShowingBooking.Application.Bookings;

public sealed record GetBookingHistoryQuery(Guid BookingId)
    : IRequest<Result<IReadOnlyList<HistoryEntryDto>>>;

public sealed class GetBookingHistoryHandler(IBookingRepository bookings)
    : IRequestHandler<GetBookingHistoryQuery, Result<IReadOnlyList<HistoryEntryDto>>>
{
    public async Task<Result<IReadOnlyList<HistoryEntryDto>>> Handle(
        GetBookingHistoryQuery request, CancellationToken ct)
    {
        if (!await bookings.ExistsAsync(request.BookingId, ct))
            return Errors.BookingNotFound;

        var entries = await bookings.GetHistoryAsync(request.BookingId, ct);

        IReadOnlyList<HistoryEntryDto> result = entries.Select(HistoryEntryDto.From).ToList();
        return Result<IReadOnlyList<HistoryEntryDto>>.Success(result);
    }
}
