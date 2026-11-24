namespace PropertyShowing.Api.Services;

public sealed class BookingService(ISlotRepository slotRepository, IBookingRepository bookingRepository)
{
    public async Task<(SlotResponse? Slot, string? Error)> CreateSlotAsync(CreateSlotRequest request, CancellationToken cancellationToken)
    {
        var slot = new ShowingSlot
        {
            Id = Guid.NewGuid(), PropertyId = request.PropertyId, StartsAt = request.StartsAt,
            DurationMinutes = request.DurationMinutes, Capacity = request.Capacity, BookedSeats = 0
        };
        await slotRepository.AddAsync(slot, cancellationToken);
        return (new SlotResponse(slot.Id, slot.PropertyId, slot.StartsAt, slot.DurationMinutes, slot.Capacity, 0, slot.Capacity), null);
    }

    public Task<PagedResponse<SlotResponse>> GetSlotsAsync(SlotQueryRequest request, CancellationToken cancellationToken) =>
        slotRepository.GetPageAsync(request, cancellationToken);

    public async Task<(BookingResponse? Booking, string? Error, int StatusCode)> CreateBookingAsync(Guid slotId, CreateBookingRequest request, CancellationToken cancellationToken)
    {
        var slot = await slotRepository.GetByIdAsync(slotId, cancellationToken);
        if (slot is null) return (null, "Showing slot was not found.", StatusCodes.Status404NotFound);
        if (slot.StartsAt <= DateTimeOffset.UtcNow) return (null, "A showing that has already started cannot be booked.", StatusCodes.Status409Conflict);

        var email = request.RenterEmail.Trim().ToLowerInvariant();
        if (await bookingRepository.HasActiveBookingAsync(slotId, email, cancellationToken))
            return (null, "The renter already has an active booking for this slot.", StatusCodes.Status409Conflict);

        var now = DateTimeOffset.UtcNow;
        var booking = new Booking
        {
            Id = Guid.NewGuid(), SlotId = slotId, RenterEmail = email,
            PartySize = request.PartySize, Status = BookingStatus.Confirmed, CreatedAt = now
        };
        booking.History.Add(new BookingHistory
        {
            Id = Guid.NewGuid(), FromStatus = BookingStatus.Confirmed, ToStatus = BookingStatus.Confirmed,
            ChangedAt = now, Actor = "renter", Reason = "Booking created"
        });

        try
        {
            await bookingRepository.ExecuteInTransactionAsync(async token =>
            {
                if (!await slotRepository.TryReserveSeatsAsync(slotId, request.PartySize, token))
                    throw new InsufficientCapacityException();
                await bookingRepository.AddAsync(booking, token);
            }, cancellationToken);
        }
        catch (InsufficientCapacityException)
        {
            return (null, "Not enough seats remain for this party.", StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException)
        {
            return (null, "An active booking already exists for this renter and slot.", StatusCodes.Status409Conflict);
        }

        return (new BookingResponse(booking.Id, booking.SlotId, booking.RenterEmail, booking.PartySize, booking.Status.ToString(), booking.CreatedAt), null, StatusCodes.Status201Created);
    }

    public async Task<(bool Success, string? Error, int StatusCode)> TransitionAsync(Guid bookingId, TransitionBookingRequest request, CancellationToken cancellationToken)
    {
        var booking = await bookingRepository.GetByIdAsync(bookingId, cancellationToken);
        if (booking is null) return (false, "Booking was not found.", StatusCodes.Status404NotFound);
        if (booking.Status != BookingStatus.Confirmed) return (false, "Only Confirmed bookings can transition.", StatusCodes.Status409Conflict);
        if (!Enum.TryParse<BookingStatus>(request.To, true, out var target) || target == BookingStatus.Confirmed)
            return (false, "The requested transition is not supported.", StatusCodes.Status409Conflict);

        var now = DateTimeOffset.UtcNow;
        var hasStarted = booking.Slot.StartsAt <= now;
        var allowed = target switch
        {
            BookingStatus.Cancelled => !hasStarted && request.Actor is "renter" or "owner",
            BookingStatus.Attended or BookingStatus.NoShow => hasStarted && request.Actor == "owner",
            _ => false
        };
        if (!allowed) return (false, "The requested actor cannot perform this transition at this time.", StatusCodes.Status409Conflict);

        try
        {
            await bookingRepository.ExecuteInTransactionAsync(async token =>
            {
                booking.Status = target;
                booking.History.Add(new BookingHistory
                {
                    Id = Guid.NewGuid(), BookingId = booking.Id, FromStatus = BookingStatus.Confirmed,
                    ToStatus = target, ChangedAt = now, Actor = request.Actor, Reason = request.Reason
                });
                await bookingRepository.SaveChangesAsync(token);
                if (target == BookingStatus.Cancelled)
                    await slotRepository.ReleaseSeatsAsync(booking.SlotId, booking.PartySize, token);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (false, "The booking was changed by another request. Refresh and try again.", StatusCodes.Status409Conflict);
        }

        return (true, null, StatusCodes.Status200OK);
    }

    public async Task<(IReadOnlyCollection<BookingHistoryResponse>? History, string? Error)> GetHistoryAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        if (!await bookingRepository.BookingExistsAsync(bookingId, cancellationToken)) return (null, "Booking was not found.");
        return (await bookingRepository.GetHistoryAsync(bookingId, cancellationToken), null);
    }

    private sealed class InsufficientCapacityException : Exception { }
}
