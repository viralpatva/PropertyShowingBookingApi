namespace PropertyShowing.Application.Services;

public sealed class BookingService(ISlotRepository slotRepository, IBookingRepository bookingRepository) : IBookingService
{

    public async Task<(BookingResponse? Booking, string? Error, int StatusCode)> CreateBookingAsync(
        Guid slotId,
        CreateBookingRequest request,
        CancellationToken cancellationToken)
    {
        var slot = await slotRepository.GetByIdAsync(slotId, cancellationToken);
        if (slot is null)
        {
            return (null, "Showing slot was not found.", (int)HttpStatusCode.NotFound);
        }

        if (slot.StartsAt <= DateTimeOffset.UtcNow)
        {
            return (null, "A showing that has already started cannot be booked.", (int)HttpStatusCode.Conflict);
        }

        var renterEmail = request.RenterEmail.Trim().ToLowerInvariant();
        if (await bookingRepository.HasActiveBookingAsync(slotId, renterEmail, cancellationToken))
        {
            return (null, "The renter already has an active booking for this slot.", (int)HttpStatusCode.Conflict);
        }

        var now = DateTimeOffset.UtcNow;
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            SlotId = slotId,
            RenterEmail = renterEmail,
            PartySize = request.PartySize,
            Status = BookingStatus.Confirmed,
            CreatedAt = now
        };
        booking.History.Add(new BookingHistory
        {
            Id = Guid.NewGuid(),
            FromStatus = BookingStatus.Confirmed,
            ToStatus = BookingStatus.Confirmed,
            ChangedAt = now,
            Actor = "renter",
            Reason = "Booking created"
        });

        try
        {
            await bookingRepository.ExecuteInTransactionAsync(async token =>
            {
                var seatsReserved = await slotRepository.TryReserveSeatsAsync(slotId, request.PartySize, token);
                if (!seatsReserved)
                {
                    throw new InsufficientCapacityException();
                }

                await bookingRepository.AddAsync(booking, token);
            }, cancellationToken);
        }
        catch (InsufficientCapacityException)
        {
            return (null, "Not enough seats remain for this party.", (int)HttpStatusCode.Conflict);
        }
        catch (DuplicateActiveBookingException)
        {
            return (null, "The renter already has an active booking for this slot.", (int)HttpStatusCode.Conflict);
        }

        return (
            new BookingResponse(booking.Id, booking.SlotId, booking.RenterEmail, booking.PartySize, booking.Status.ToString(), booking.CreatedAt),
            null,
            (int)HttpStatusCode.Created);
    }

    public async Task<(bool Success, string? Error, int StatusCode)> TransitionAsync(
        Guid bookingId,
        TransitionBookingRequest request,
        CancellationToken cancellationToken)
    {
        var booking = await bookingRepository.GetByIdAsync(bookingId, cancellationToken);
        if (booking is null)
        {
            return (false, "Booking was not found.", (int)HttpStatusCode.NotFound);
        }

        if (booking.Status != BookingStatus.Confirmed)
        {
            return (false, "Only Confirmed bookings can transition.", (int)HttpStatusCode.Conflict);
        }

        if (!Enum.TryParse<BookingStatus>(request.To, true, out var target) || target == BookingStatus.Confirmed)
        {
            return (false, "The requested transition is not supported.", (int)HttpStatusCode.Conflict);
        }

        var now = DateTimeOffset.UtcNow;
        var hasStarted = booking.Slot.StartsAt <= now;
        var allowed = target switch
        {
            BookingStatus.Cancelled => !hasStarted && request.Actor is "renter" or "owner",
            BookingStatus.Attended or BookingStatus.NoShow => hasStarted && request.Actor == "owner",
            _ => false
        };

        if (!allowed)
        {
            return (false, "The requested actor cannot perform this transition at this time.", (int)HttpStatusCode.Conflict);
        }

        try
        {
            await bookingRepository.ExecuteInTransactionAsync(async token =>
            {
                booking.Status = target;
                await bookingRepository.AddHistoryAsync(
                    new BookingHistory
                    {
                        Id = Guid.NewGuid(),
                        BookingId = booking.Id,
                        FromStatus = BookingStatus.Confirmed,
                        ToStatus = target,
                        ChangedAt = now,
                        Actor = request.Actor,
                        Reason = request.Reason
                    },
                    token);

                await bookingRepository.SaveChangesAsync(token);
                if (target == BookingStatus.Cancelled)
                {
                    await slotRepository.ReleaseSeatsAsync(booking.SlotId, booking.PartySize, token);
                }
            }, cancellationToken);
        }
        catch (BookingConcurrencyException)
        {
            return (false, "The booking was changed by another request. Refresh and try again.", (int)HttpStatusCode.Conflict);
        }

        return (true, null, (int)HttpStatusCode.OK);
    }

    public async Task<(IReadOnlyCollection<BookingHistoryResponse>? History, string? Error)> GetHistoryAsync(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        var exists = await bookingRepository.BookingExistsAsync(bookingId, cancellationToken);
        if (!exists)
        {
            return (null, "Booking was not found.");
        }

        return (await bookingRepository.GetHistoryAsync(bookingId, cancellationToken), null);
    }

    private sealed class InsufficientCapacityException : Exception { }
}
