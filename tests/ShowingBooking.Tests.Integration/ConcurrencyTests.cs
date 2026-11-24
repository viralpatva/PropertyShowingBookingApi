using System.Net;

namespace ShowingBooking.Tests.Integration;

/// <summary>
/// Fires many bookings at the same moment against the real app and a real SQLite file,
/// then checks the one thing that must never happen: more seats used than the slot has.
/// </summary>
public class ConcurrencyTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Parallel_bookings_for_the_last_seats_never_overbook_the_slot()
    {
        const int capacity = 5;
        const int renters = 20;

        var propertyId = $"property-{Guid.NewGuid():N}";
        var slotId = (await CreateSlotAsync(propertyId, capacity: capacity)).GetProperty("id").GetGuid();

        // Hold every request at a starting line so they really overlap.
        var startingLine = new TaskCompletionSource();
        var attempts = Enumerable.Range(0, renters).Select(async i =>
        {
            await startingLine.Task;
            return await BookAsync(slotId, $"renter{i}@example.com", 1);
        }).ToList();
        startingLine.SetResult();

        var responses = await Task.WhenAll(attempts);

        // Every answer is either "booked" or a clean 409. Never a 500.
        Assert.All(responses, r => Assert.True(
            r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
            $"Unexpected status {(int)r.StatusCode}"));

        var booked = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        Assert.InRange(booked, 1, capacity);

        // The slot agrees with what we were told: seats used == successful bookings, never above capacity.
        var seatsRemaining = await SeatsRemainingAsync(propertyId);
        Assert.True(seatsRemaining >= 0, "The slot was overbooked.");
        Assert.Equal(capacity - booked, seatsRemaining);
    }

    [Fact]
    public async Task Parallel_bookings_with_larger_parties_never_overbook_the_slot()
    {
        const int capacity = 5;

        var propertyId = $"property-{Guid.NewGuid():N}";
        var slotId = (await CreateSlotAsync(propertyId, capacity: capacity)).GetProperty("id").GetGuid();

        // Ten parties of 3 compete for 5 seats: at most one of them can fit.
        var startingLine = new TaskCompletionSource();
        var attempts = Enumerable.Range(0, 10).Select(async i =>
        {
            await startingLine.Task;
            return await BookAsync(slotId, $"party{i}@example.com", 3);
        }).ToList();
        startingLine.SetResult();

        var responses = await Task.WhenAll(attempts);

        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict));
        Assert.True(responses.Count(r => r.StatusCode == HttpStatusCode.Created) <= 1);
        Assert.True(await SeatsRemainingAsync(propertyId) >= 0);
    }

    [Fact]
    public async Task Same_renter_booking_twice_at_once_gets_only_one_active_booking()
    {
        var propertyId = $"property-{Guid.NewGuid():N}";
        var slotId = (await CreateSlotAsync(propertyId, capacity: 10)).GetProperty("id").GetGuid();

        var startingLine = new TaskCompletionSource();
        var attempts = Enumerable.Range(0, 6).Select(async _ =>
        {
            await startingLine.Task;
            return await BookAsync(slotId, "same@example.com", 2);
        }).ToList();
        startingLine.SetResult();

        var responses = await Task.WhenAll(attempts);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(8, await SeatsRemainingAsync(propertyId)); // only one party of 2 holds seats
    }
}
