using FluentValidation;
using MediatR;
using ShowingBooking.Application.Abstractions;
using ShowingBooking.Application.Common;
using ShowingBooking.Domain;

namespace ShowingBooking.Application.Bookings;

/// <summary>To and Actor arrive as text so that bad values give a clear 400 instead of a binding error.</summary>
public sealed record TransitionBookingCommand(Guid BookingId, string To, string Actor, string? Reason)
    : IRequest<Result<BookingDto>>;

public sealed class TransitionBookingCommandValidator : AbstractValidator<TransitionBookingCommand>
{
    public const int MaxReasonLength = 500;

    public TransitionBookingCommandValidator()
    {
        RuleFor(x => x.To)
            .Must(ValidationExtensions.IsEnumName<BookingStatus>)
            .WithMessage($"to must be one of: {string.Join(", ", Enum.GetNames<BookingStatus>())}.");
        RuleFor(x => x.Actor)
            .Must(ValidationExtensions.IsEnumName<Actor>)
            .WithMessage("actor must be 'owner' or 'renter'.");
        RuleFor(x => x.Reason)
            .MaximumLength(MaxReasonLength)
            .WithMessage($"reason must be at most {MaxReasonLength} characters.");
    }
}

public sealed class TransitionBookingHandler(
    IBookingRepository bookings,
    IUnitOfWork unitOfWork,
    IValidator<TransitionBookingCommand> validator,
    TimeProvider clock) : IRequestHandler<TransitionBookingCommand, Result<BookingDto>>
{
    public async Task<Result<BookingDto>> Handle(TransitionBookingCommand request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return validation.ToError();

        // Safe to parse: the validator already checked both values.
        var to = Enum.Parse<BookingStatus>(request.To.Trim(), ignoreCase: true);
        var actor = Enum.Parse<Actor>(request.Actor.Trim(), ignoreCase: true);

        return await OptimisticRetry.RunAsync<BookingDto>(unitOfWork, async () =>
        {
            // The slot is loaded too: cancelling gives its seats back, and the slot's
            // version token makes two racing cancellations or bookings safe.
            var booking = await bookings.GetWithSlotAsync(request.BookingId, ct);

            if (booking is null)
                return Errors.BookingNotFound;

            var transitioned = booking.Transition(to, actor, request.Reason, clock.GetUtcNow());
            if (!transitioned.IsSuccess)
                return transitioned.Error!;

            await unitOfWork.SaveChangesAsync(ct);

            return BookingDto.From(booking);
        });
    }
}
