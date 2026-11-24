namespace PropertyShowing.Api.Controllers;

[ApiController]
[Route("bookings")]
public sealed class BookingsController(IBookingService bookingService) : ControllerBase
{
    [HttpPost("{id:guid}/transitions")]
    public async Task<IActionResult> Transition(Guid id, [FromBody] TransitionBookingRequest request, CancellationToken cancellationToken)
    {
        var result = await bookingService.TransitionAsync(id, request, cancellationToken);
        if (result.Success) return Ok(new { message = "Booking status updated." });
        return result.StatusCode == StatusCodes.Status404NotFound
            ? NotFound(new ApiError(result.Error!))
            : Conflict(new ApiError(result.Error!));
    }

    [HttpGet("{id:guid}/history")]
    public async Task<IActionResult> History(Guid id, CancellationToken cancellationToken)
    {
        var result = await bookingService.GetHistoryAsync(id, cancellationToken);
        return result.History is not null ? Ok(result.History) : NotFound(new ApiError(result.Error!));
    }
}
