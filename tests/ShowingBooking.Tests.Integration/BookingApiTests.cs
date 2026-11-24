using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ShowingBooking.Tests.Integration;

public class BookingApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    // ---- the main flow: create slot -> book -> cancel -> history ----

    [Fact]
    public async Task Create_slot_then_book_then_cancel_then_read_history()
    {
        var propertyId = $"property-{Guid.NewGuid():N}";
        var slot = await CreateSlotAsync(propertyId, capacity: 5);
        var slotId = slot.GetProperty("id").GetGuid();
        Assert.Equal(5, slot.GetProperty("seatsRemaining").GetInt32());

        // Book a party of 2: seats are used.
        var bookResponse = await BookAsync(slotId, "Renter@Example.com", 2);
        Assert.Equal(HttpStatusCode.Created, bookResponse.StatusCode);
        var booking = await ReadAsync(bookResponse);
        var bookingId = booking.GetProperty("id").GetGuid();
        Assert.Equal("Confirmed", booking.GetProperty("status").GetString());
        Assert.Equal("renter@example.com", booking.GetProperty("renterEmail").GetString()); // normalized
        Assert.Equal(3, await SeatsRemainingAsync(propertyId));

        // Cancel before the slot starts: seats come back.
        var cancelResponse = await TransitionAsync(bookingId, "Cancelled", "renter", "changed my mind");
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        Assert.Equal("Cancelled", (await ReadAsync(cancelResponse)).GetProperty("status").GetString());
        Assert.Equal(5, await SeatsRemainingAsync(propertyId));

        // History has the creation entry and the cancellation, in order.
        var historyResponse = await Client.GetAsync($"/bookings/{bookingId}/history");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = await ReadAsync(historyResponse);
        Assert.Equal(2, history.GetArrayLength());

        var created = history[0];
        Assert.Equal(JsonValueKind.Null, created.GetProperty("fromStatus").ValueKind);
        Assert.Equal("Confirmed", created.GetProperty("toStatus").GetString());
        Assert.Equal("Renter", created.GetProperty("actor").GetString());

        var cancelled = history[1];
        Assert.Equal("Confirmed", cancelled.GetProperty("fromStatus").GetString());
        Assert.Equal("Cancelled", cancelled.GetProperty("toStatus").GetString());
        Assert.Equal("Renter", cancelled.GetProperty("actor").GetString());
        Assert.Equal("changed my mind", cancelled.GetProperty("reason").GetString());
        Assert.Equal(Now, cancelled.GetProperty("occurredAt").GetDateTimeOffset());
    }

    // ---- 400: bad input ----

    [Theory]
    [InlineData(-60, 30, 5)] // startsAt in the past
    [InlineData(60, 14, 5)]  // duration too short
    [InlineData(60, 121, 5)] // duration too long
    [InlineData(60, 30, 0)]  // capacity too small
    [InlineData(60, 30, 21)] // capacity too large
    public async Task Create_slot_with_bad_input_returns_400(int startsInMinutes, int duration, int capacity)
    {
        var response = await Client.PostAsJsonAsync("/slots", new
        {
            propertyId = "property-1",
            startsAt = Now.AddMinutes(startsInMinutes),
            durationMinutes = duration,
            capacity
        });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Theory]
    [InlineData("renter@example.com", 0)]
    [InlineData("renter@example.com", 7)]
    [InlineData("not-an-email", 2)]
    [InlineData("", 2)]
    public async Task Book_with_bad_input_returns_400(string email, int partySize)
    {
        var slot = await CreateSlotAsync();

        var response = await BookAsync(slot.GetProperty("id").GetGuid(), email, partySize);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Theory]
    [InlineData("Banana", "owner")]  // unknown state
    [InlineData("1", "owner")]       // numbers are not accepted
    [InlineData("Cancelled", "admin")] // unknown actor
    [InlineData("Cancelled", "")]    // missing actor
    public async Task Transition_with_bad_input_returns_400(string to, string actor)
    {
        var slot = await CreateSlotAsync();
        var bookingId = await BookSuccessfullyAsync(slot.GetProperty("id").GetGuid(), "a@example.com", 1);

        var response = await TransitionAsync(bookingId, to, actor);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Theory]
    [InlineData("pageSize=101")]
    [InlineData("pageSize=0")]
    [InlineData("page=0")]
    public async Task List_slots_with_bad_paging_returns_400(string query)
    {
        var response = await Client.GetAsync($"/slots?{query}");

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    // ---- 404: missing things ----

    [Fact]
    public async Task Book_unknown_slot_returns_404()
    {
        var response = await BookAsync(Guid.NewGuid(), "a@example.com", 1);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "slot_not_found");
    }

    [Fact]
    public async Task Transition_unknown_booking_returns_404()
    {
        var response = await TransitionAsync(Guid.NewGuid(), "Cancelled", "renter");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "booking_not_found");
    }

    [Fact]
    public async Task History_of_unknown_booking_returns_404()
    {
        var response = await Client.GetAsync($"/bookings/{Guid.NewGuid()}/history");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "booking_not_found");
    }

    // ---- 409: rules that forbid the action ----

    [Fact]
    public async Task Booking_more_seats_than_remaining_returns_409()
    {
        var slot = await CreateSlotAsync(capacity: 4);
        var slotId = slot.GetProperty("id").GetGuid();
        await BookSuccessfullyAsync(slotId, "a@example.com", 3);

        var response = await BookAsync(slotId, "b@example.com", 2);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "not_enough_seats");
    }

    [Fact]
    public async Task Second_active_booking_by_same_renter_returns_409_but_rebooking_after_cancel_works()
    {
        var slot = await CreateSlotAsync();
        var slotId = slot.GetProperty("id").GetGuid();
        var firstId = await BookSuccessfullyAsync(slotId, "a@example.com", 1);

        await AssertProblemAsync(
            await BookAsync(slotId, "A@EXAMPLE.com", 1), HttpStatusCode.Conflict, "duplicate_active_booking");

        await TransitionAsync(firstId, "Cancelled", "renter");
        Assert.Equal(HttpStatusCode.Created, (await BookAsync(slotId, "a@example.com", 1)).StatusCode);
    }

    [Fact]
    public async Task Booking_a_slot_that_has_started_returns_409()
    {
        var slot = await CreateSlotAsync();
        Factory.Clock.SetNow(DefaultStart);

        var response = await BookAsync(slot.GetProperty("id").GetGuid(), "a@example.com", 1);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "slot_started");
    }

    [Fact]
    public async Task Cancelling_exactly_at_start_returns_409_and_keeps_the_seats()
    {
        var propertyId = $"property-{Guid.NewGuid():N}";
        var slot = await CreateSlotAsync(propertyId, capacity: 5);
        var bookingId = await BookSuccessfullyAsync(slot.GetProperty("id").GetGuid(), "a@example.com", 2);
        Factory.Clock.SetNow(DefaultStart);

        var response = await TransitionAsync(bookingId, "Cancelled", "owner");

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "too_late_to_cancel");
        Assert.Equal(3, await SeatsRemainingAsync(propertyId));
    }

    [Fact]
    public async Task Attended_and_noshow_rules_follow_actor_and_timing()
    {
        var slot = await CreateSlotAsync();
        var bookingId = await BookSuccessfullyAsync(slot.GetProperty("id").GetGuid(), "a@example.com", 1);

        // Before the start, even the owner cannot mark the outcome.
        await AssertProblemAsync(
            await TransitionAsync(bookingId, "Attended", "owner"), HttpStatusCode.Conflict, "too_early_to_complete");

        Factory.Clock.SetNow(DefaultStart);

        // At the start, the renter still cannot, but the owner can.
        await AssertProblemAsync(
            await TransitionAsync(bookingId, "NoShow", "renter"), HttpStatusCode.Conflict, "actor_not_allowed");
        var ok = await TransitionAsync(bookingId, "Attended", "owner", "arrived on time");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("Attended", (await ReadAsync(ok)).GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("Cancelled")]
    [InlineData("Attended")]
    [InlineData("NoShow")]
    [InlineData("Confirmed")]
    public async Task Final_states_cannot_change_and_get_409(string target)
    {
        var slot = await CreateSlotAsync();
        var bookingId = await BookSuccessfullyAsync(slot.GetProperty("id").GetGuid(), "a@example.com", 1);
        await TransitionAsync(bookingId, "Cancelled", "renter"); // now final

        var response = await TransitionAsync(bookingId, target, "owner");

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "invalid_transition");
    }

    // ---- GET /slots ----

    [Fact]
    public async Task List_slots_filters_orders_and_pages()
    {
        var propertyId = $"property-{Guid.NewGuid():N}";
        // Created out of order on purpose.
        await CreateSlotAsync(propertyId, DefaultStart.AddHours(3));
        await CreateSlotAsync(propertyId, DefaultStart.AddHours(1));
        await CreateSlotAsync(propertyId, DefaultStart.AddHours(2));
        await CreateSlotAsync($"other-{Guid.NewGuid():N}", DefaultStart.AddHours(1)); // different property

        var firstPage = await ReadAsync(await Client.GetAsync($"/slots?propertyId={propertyId}&page=1&pageSize=2"));
        var secondPage = await ReadAsync(await Client.GetAsync($"/slots?propertyId={propertyId}&page=2&pageSize=2"));

        Assert.Equal(3, firstPage.GetProperty("totalCount").GetInt32());
        var first = firstPage.GetProperty("items");
        Assert.Equal(2, first.GetArrayLength());
        Assert.Equal(DefaultStart.AddHours(1), first[0].GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(DefaultStart.AddHours(2), first[1].GetProperty("startsAt").GetDateTimeOffset());

        var second = secondPage.GetProperty("items");
        Assert.Equal(1, second.GetArrayLength());
        Assert.Equal(DefaultStart.AddHours(3), second[0].GetProperty("startsAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task List_slots_from_is_inclusive_and_to_is_exclusive()
    {
        var propertyId = $"property-{Guid.NewGuid():N}";
        await CreateSlotAsync(propertyId, DefaultStart.AddHours(1));
        await CreateSlotAsync(propertyId, DefaultStart.AddHours(2));
        await CreateSlotAsync(propertyId, DefaultStart.AddHours(3));

        var from = Uri.EscapeDataString(DefaultStart.AddHours(1).ToString("o"));
        var to = Uri.EscapeDataString(DefaultStart.AddHours(3).ToString("o"));
        var page = await ReadAsync(await Client.GetAsync($"/slots?propertyId={propertyId}&from={from}&to={to}"));

        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
    }
}
