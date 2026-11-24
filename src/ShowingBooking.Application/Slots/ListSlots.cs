using FluentValidation;
using MediatR;
using ShowingBooking.Application.Abstractions;
using ShowingBooking.Application.Common;
using ShowingBooking.Domain;

namespace ShowingBooking.Application.Slots;

public sealed record ListSlotsQuery(
    string? PropertyId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page,
    int PageSize) : IRequest<Result<PagedResult<SlotDto>>>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
}

public sealed class ListSlotsQueryValidator : AbstractValidator<ListSlotsQuery>
{
    public ListSlotsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage("page must be 1 or more.");
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, ListSlotsQuery.MaxPageSize)
            .WithMessage($"pageSize must be between 1 and {ListSlotsQuery.MaxPageSize}.");
        RuleFor(x => x)
            .Must(q => q.From is null || q.To is null || q.From < q.To)
            .WithMessage("from must be earlier than to.");
    }
}

public sealed class ListSlotsHandler(
    ISlotRepository slots,
    IValidator<ListSlotsQuery> validator) : IRequestHandler<ListSlotsQuery, Result<PagedResult<SlotDto>>>
{
    public async Task<Result<PagedResult<SlotDto>>> Handle(ListSlotsQuery request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return validation.ToError();

        var (items, totalCount) = await slots.ListAsync(
            request.PropertyId, request.From, request.To, request.Page, request.PageSize, ct);

        return new PagedResult<SlotDto>(
            items.Select(SlotDto.From).ToList(), request.Page, request.PageSize, totalCount);
    }
}
