using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropertyShowing.Api.Data;
using PropertyShowing.Api.Models;

namespace PropertyShowing.Tests;

public sealed class BookingApiTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"booking-tests-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;

    public BookingApiTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(x => x.ServiceType == typeof(DbContextOptions<BookingDbContext>));
            if (descriptor is not null) services.Remove(descriptor);
            services.AddDbContext<BookingDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        }));
    }

    [Fact]
    public async Task CreateSlot_RejectsDurationOutsideAllowedRange()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/slots", new
        {
            propertyId = Guid.NewGuid(), startsAt = DateTimeOffset.UtcNow.AddDays(1), durationMinutes = 10, capacity = 5
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Booking_CannotExceedCapacity_AndCancellationFreesSeats()
    {
        var client = _factory.CreateClient();
        var slot = await CreateSlotAsync(client, 3);
        var first = await client.PostAsJsonAsync($"/slots/{slot.Id}/bookings", new { renterEmail = "one@example.com", partySize = 2 });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var rejected = await client.PostAsJsonAsync($"/slots/{slot.Id}/bookings", new { renterEmail = "two@example.com", partySize = 2 });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        var booking = await first.Content.ReadFromJsonAsync<BookingResponse>();
        Assert.NotNull(booking);
        var cancellation = await client.PostAsJsonAsync($"/bookings/{booking.Id}/transitions", new { to = "Cancelled", actor = "renter", reason = "Plans changed" });
        Assert.Equal(HttpStatusCode.OK, cancellation.StatusCode);
        var replacement = await client.PostAsJsonAsync($"/slots/{slot.Id}/bookings", new { renterEmail = "two@example.com", partySize = 3 });
        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
    }

    [Fact]
    public async Task SameRenter_CannotCreateTwoActiveBookingsForOneSlot()
    {
        var client = _factory.CreateClient();
        var slot = await CreateSlotAsync(client, 6);
        var first = await client.PostAsJsonAsync($"/slots/{slot.Id}/bookings", new { renterEmail = "same@example.com", partySize = 1 });
        var second = await client.PostAsJsonAsync($"/slots/{slot.Id}/bookings", new { renterEmail = "same@example.com", partySize = 1 });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task OwnerCannotMarkAttendedBeforeSlotStarts()
    {
        var client = _factory.CreateClient();
        var slot = await CreateSlotAsync(client, 3);
        var bookingResponse = await client.PostAsJsonAsync($"/slots/{slot.Id}/bookings", new { renterEmail = "future@example.com", partySize = 1 });
        bookingResponse.EnsureSuccessStatusCode();
        var booking = await bookingResponse.Content.ReadFromJsonAsync<BookingResponse>();
        Assert.NotNull(booking);
        var transition = await client.PostAsJsonAsync($"/bookings/{booking.Id}/transitions", new { to = "Attended", actor = "owner", reason = "Early mark" });
        Assert.Equal(HttpStatusCode.Conflict, transition.StatusCode);
    }

    private static async Task<SlotResponse> CreateSlotAsync(HttpClient client, int capacity)
    {
        var response = await client.PostAsJsonAsync("/slots", new
        {
            propertyId = Guid.NewGuid(), startsAt = DateTimeOffset.UtcNow.AddDays(1), durationMinutes = 30, capacity
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SlotResponse>())!;
    }

    public void Dispose()
    {
        _factory.Dispose();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
    }
}
