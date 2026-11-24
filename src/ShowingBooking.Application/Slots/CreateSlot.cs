using FluentValidation;
using MediatR;
using ShowingBooking.Application.Abstractions;
using ShowingBooking.Application.Common;
using ShowingBooking.Domain;

namespace ShowingBooking.Application.Slots;

public sealed record CreateSlotCommand(
    string PropertyId,
    DateTimeOffset StartsAt,
    int DurationMinutes,
    int Capacity) : IRequest<Result<SlotDto>>;

public sealed class CreateSlotCommandValidator : AbstractValidator<CreateSlotCommand>
{
    public CreateSlotCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.PropertyId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.StartsAt)
            .Must(startsAt => startsAt > clock.GetUtcNow())
            .WithMessage("startsAt must be in the future.");
        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(Slot.MinDurationMinutes, Slot.MaxDurationMinutes)
            .WithMessage($"durationMinutes must be between {Slot.MinDurationMinutes} and {Slot.MaxDurationMinutes}.");
        RuleFor(x => x.Capacity)
            .InclusiveBetween(Slot.MinCapacity, Slot.MaxCapacity)
            .WithMessage($"capacity must be between {Slot.MinCapacity} and {Slot.MaxCapacity}.");
    }
}

public sealed class CreateSlotHandler(
    ISlotRepository slots,
    IUnitOfWork unitOfWork,
    IValidator<CreateSlotCommand> validator,
    TimeProvider clock) : IRequestHandler<CreateSlotCommand, Result<SlotDto>>
{
    public async Task<Result<SlotDto>> Handle(CreateSlotCommand request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return validation.ToError();

        var created = Slot.Create(
            request.PropertyId, request.StartsAt, request.DurationMinutes, request.Capacity, clock.GetUtcNow());
        if (!created.IsSuccess)
            return created.Error!;

        slots.Add(created.Value);
        await unitOfWork.SaveChangesAsync(ct);

        return SlotDto.From(created.Value);
    }
}
