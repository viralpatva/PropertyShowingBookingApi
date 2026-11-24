using ShowingBooking.Domain;

namespace ShowingBooking.Application;

public sealed record SlotDto(
    Guid Id,
    string PropertyId,
    DateTimeOffset StartsAt,
    int DurationMinutes,
    int Capacity,
    int SeatsRemaining)
{
    public static SlotDto From(Slot slot) => new(
        slot.Id, slot.PropertyId, slot.StartsAt, slot.DurationMinutes, slot.Capacity, slot.SeatsRemaining);
}

public sealed record BookingDto(
    Guid Id,
    Guid SlotId,
    string RenterEmail,
    int PartySize,
    string Status,
    DateTimeOffset CreatedAt)
{
    public static BookingDto From(Booking booking) => new(
        booking.Id, booking.SlotId, booking.RenterEmail, booking.PartySize,
        booking.Status.ToString(), booking.CreatedAt);
}

public sealed record HistoryEntryDto(
    string? FromStatus,
    string ToStatus,
    string Actor,
    string? Reason,
    DateTimeOffset OccurredAt)
{
    public static HistoryEntryDto From(BookingHistoryEntry entry) => new(
        entry.FromStatus?.ToString(), entry.ToStatus.ToString(), entry.Actor.ToString(),
        entry.Reason, entry.OccurredAt);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
