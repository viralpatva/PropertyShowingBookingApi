using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShowingBooking.Application.Bookings;
using ShowingBooking.Application.Slots;

namespace ShowingBooking.Api;

// Request bodies. Everything is nullable so a missing field becomes a clear 400 from the
// validators rather than a framework binding error.
public sealed record CreateSlotRequest(string? PropertyId, DateTimeOffset StartsAt, int DurationMinutes, int Capacity);

public sealed record BookSlotRequest(string? RenterEmail, int PartySize);

public sealed record TransitionRequest(string? To, string? Reason, string? Actor);

public static class ShowingEndpoints
{
    public static IEndpointRouteBuilder MapShowingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/slots", async (CreateSlotRequest body, ISender mediator, CancellationToken ct) =>
        {
            var command = new CreateSlotCommand(
                body.PropertyId ?? string.Empty, body.StartsAt, body.DurationMinutes, body.Capacity);

            var result = await mediator.Send(command, ct);
            return result.ToHttp(slot => Results.Json(slot, statusCode: StatusCodes.Status201Created));
        });

        app.MapGet("/slots", async (
            string? propertyId,
            [FromQuery(Name = "from")] DateTimeOffset? fromTime,
            [FromQuery(Name = "to")] DateTimeOffset? toTime,
            int? page,
            int? pageSize,
            ISender mediator,
            CancellationToken ct) =>
        {
            var query = new ListSlotsQuery(
                propertyId,
                fromTime,
                toTime,
                page ?? 1,
                pageSize ?? ListSlotsQuery.DefaultPageSize);

            var result = await mediator.Send(query, ct);
            return result.ToHttp(slots => Results.Ok(slots));
        });

        app.MapPost("/slots/{id:guid}/bookings", async (
            Guid id, BookSlotRequest body, ISender mediator, CancellationToken ct) =>
        {
            var command = new BookSlotCommand(id, body.RenterEmail ?? string.Empty, body.PartySize);

            var result = await mediator.Send(command, ct);
            return result.ToHttp(booking => Results.Json(booking, statusCode: StatusCodes.Status201Created));
        });

        app.MapPost("/bookings/{id:guid}/transitions", async (
            Guid id, TransitionRequest body, ISender mediator, CancellationToken ct) =>
        {
            var command = new TransitionBookingCommand(
                id, body.To ?? string.Empty, body.Actor ?? string.Empty, body.Reason);

            var result = await mediator.Send(command, ct);
            return result.ToHttp(booking => Results.Ok(booking));
        });

        app.MapGet("/bookings/{id:guid}/history", async (Guid id, ISender mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetBookingHistoryQuery(id), ct);
            return result.ToHttp(history => Results.Ok(history));
        });

        return app;
    }
}
