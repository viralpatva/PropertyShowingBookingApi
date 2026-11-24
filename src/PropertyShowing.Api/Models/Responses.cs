namespace PropertyShowing.Api.Models;
public sealed record SlotResponse(Guid Id, Guid PropertyId, DateTimeOffset StartsAt, int DurationMinutes, int Capacity, int BookedSeats, int SeatsRemaining);
public sealed record BookingResponse(Guid Id, Guid SlotId, string RenterEmail, int PartySize, string Status, DateTimeOffset CreatedAt);
public sealed record BookingHistoryResponse(string From, string To, DateTimeOffset ChangedAt, string Actor, string? Reason);
public sealed record PagedResponse<T>(IReadOnlyCollection<T> Items, int Page, int PageSize, int TotalCount);
public sealed record ApiError(string Message);
