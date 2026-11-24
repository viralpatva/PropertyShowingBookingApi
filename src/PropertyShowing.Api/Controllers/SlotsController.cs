namespace PropertyShowing.Api.Controllers;

[ApiController]
[Route("slots")]
public sealed class SlotsController(BookingService bookingService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<SlotResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateSlotRequest request, CancellationToken cancellationToken)
    {
        var result = await bookingService.CreateSlotAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { propertyId = request.PropertyId }, result.Slot);
    }

    [HttpGet]
    [ProducesResponseType<PagedResponse<SlotResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] SlotQueryRequest request, CancellationToken cancellationToken) =>
        Ok(await bookingService.GetSlotsAsync(request, cancellationToken));

    [HttpPost("{id:guid}/bookings")]
    [ProducesResponseType<BookingResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Book(Guid id, [FromBody] CreateBookingRequest request, CancellationToken cancellationToken)
    {
        var result = await bookingService.CreateBookingAsync(id, request, cancellationToken);
        if (result.Booking is not null) return StatusCode(StatusCodes.Status201Created, result.Booking);
        return result.StatusCode == StatusCodes.Status404NotFound
            ? NotFound(new ApiError(result.Error!))
            : Conflict(new ApiError(result.Error!));
    }
}
