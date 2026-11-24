using FluentValidation;
using MediatR;
using ShowingBooking.Application.Abstractions;
using ShowingBooking.Application.Common;
using ShowingBooking.Domain;

namespace ShowingBooking.Application.Bookings;

public sealed record BookSlotCommand(Guid SlotId, string RenterEmail, int PartySize)
    : IRequest<Result<BookingDto>>;

public sealed class BookSlotCommandValidator : AbstractValidator<BookSlotCommand>
{
    public BookSlotCommandValidator()
    {
        RuleFor(x => x.RenterEmail)
            .NotEmpty().WithMessage("renterEmail is required.")
            .MaximumLength(254).WithMessage("renterEmail is too long.")
            .EmailAddress().WithMessage("renterEmail must be a valid email address.");
        RuleFor(x => x.PartySize)
            .InclusiveBetween(Booking.MinPartySize, Booking.MaxPartySize)
            .WithMessage($"partySize must be between {Booking.MinPartySize} and {Booking.MaxPartySize}.");
    }
}

public sealed class BookSlotHandler(
    ISlotRepository slots,
    IUnitOfWork unitOfWork,
    IValidator<BookSlotCommand> validator,
    TimeProvider clock) : IRequestHandler<BookSlotCommand, Result<BookingDto>>
{
    public async Task<Result<BookingDto>> Handle(BookSlotCommand request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return validation.ToError();

        return await OptimisticRetry.RunAsync<BookingDto>(unitOfWork, async () =>
        {
            // Load the slot together with its Confirmed bookings (needed for the duplicate check).
            var slot = await slots.GetWithConfirmedBookingsAsync(request.SlotId, ct);

            if (slot is null)
                return Errors.SlotNotFound;

            var booked = slot.Book(request.RenterEmail, request.PartySize, clock.GetUtcNow());
            if (!booked.IsSuccess)
                return booked.Error!;

            // One atomic save: slot seat count + version, new booking, first history entry.
            // If another request changed the slot since we loaded it, this throws
            // ConcurrencyConflictException and OptimisticRetry runs the whole thing again.
            await unitOfWork.SaveChangesAsync(ct);

            return BookingDto.From(booked.Value);
        });
    }
}
