using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ShowingBooking.Tests.Integration;

public abstract class ApiTestBase : IClassFixture<ApiFactory>
{
    // Every test starts at this moment. Slots are created one day later.
    public static readonly DateTimeOffset Now = new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);

    protected ApiFactory Factory { get; }
    protected HttpClient Client { get; }

    protected ApiTestBase(ApiFactory factory)
    {
        Factory = factory;
        Factory.Clock.SetNow(Now);
        Client = factory.CreateClient();
    }

    protected static DateTimeOffset DefaultStart => Now.AddDays(1);

    protected async Task<JsonElement> CreateSlotAsync(
        string? propertyId = null, DateTimeOffset? startsAt = null, int capacity = 5, int durationMinutes = 30)
    {
        var response = await Client.PostAsJsonAsync("/slots", new
        {
            propertyId = propertyId ?? $"property-{Guid.NewGuid():N}",
            startsAt = startsAt ?? DefaultStart,
            durationMinutes,
            capacity
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    protected Task<HttpResponseMessage> BookAsync(Guid slotId, string email, int partySize) =>
        Client.PostAsJsonAsync($"/slots/{slotId}/bookings", new { renterEmail = email, partySize });

    protected Task<HttpResponseMessage> TransitionAsync(Guid bookingId, string to, string actor, string? reason = null) =>
        Client.PostAsJsonAsync($"/bookings/{bookingId}/transitions", new { to, actor, reason });

    protected async Task<Guid> BookSuccessfullyAsync(Guid slotId, string email, int partySize)
    {
        var response = await BookAsync(slotId, email, partySize);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    protected async Task<int> SeatsRemainingAsync(string propertyId)
    {
        var page = await ReadAsync(await Client.GetAsync($"/slots?propertyId={propertyId}"));
        return page.GetProperty("items")[0].GetProperty("seatsRemaining").GetInt32();
    }

    protected static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Checks the status code and the stable machine-readable error code of a ProblemDetails body.</summary>
    protected static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await ReadAsync(response);
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }
}
