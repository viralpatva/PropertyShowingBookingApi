
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
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<BookingDbContext>(options => options.UseSqlite($"Data Source={_databasePath};Pooling=False"));
        }));
    }

    [Fact]
    public async Task CreateSlot_RejectsDurationOutsideAllowedRange()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/slots", new
        {
            propertyId = Guid.NewGuid(),
            startsAt = DateTimeOffset.UtcNow.AddDays(1),
            durationMinutes = 10,
            capacity = 5
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Booking_CannotExceedSlotCapacity_AndCancellationFreesSeats()
    {
        using var client = _factory.CreateClient();
        var slot = await CreateSlotAsync(client, capacity: 3);

        var first = await client.PostAsJsonAsync($"/slots/{slot.Id}/bookings", new { renterEmail = "one@example.com", partySize = 2 });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var rejected = await client.PostAsJsonAsync($"/slots/{slot.Id}/bookings", new { renterEmail = "two@example.com", partySize = 2 });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

        var booking = await first.Content.ReadFromJsonAsync<BookingResponse>();
        Assert.NotNull(booking);
        var cancellation = await client.PostAsJsonAsync($"/bookings/{booking.Id}/transitions", new { to = "Cancelled", actor = "renter", reason = "Plans changed" });
        var cancellationBody = await cancellation.Content.ReadAsStringAsync();

        Assert.True(
            cancellation.StatusCode == HttpStatusCode.OK,
            $"Cancellation failed: {(int)cancellation.StatusCode}. Response: {cancellationBody}");

        var afterCancellation = await client.PostAsJsonAsync($"/slots/{slot.Id}/bookings", new { renterEmail = "two@example.com", partySize = 3 });
        var bookingBody = await afterCancellation.Content.ReadAsStringAsync();

        Assert.True(
            afterCancellation.StatusCode == HttpStatusCode.Created,
            $"Rebooking failed: {(int)afterCancellation.StatusCode}. Response: {bookingBody}");
    }


    [Fact]
    public async Task ConcurrentBookings_CannotOverbookTheLastSeats()
    {
        using var client = _factory.CreateClient();
        const int slotCapacity = 3;
        const int concurrentRenterCount = 12;

        var slot = await CreateSlotAsync(client, capacity: slotCapacity);
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var bookingTasks = Enumerable.Range(1, concurrentRenterCount)
            .Select(async renterNumber =>
            {
                await startGate.Task;

                return await client.PostAsJsonAsync(
                    $"/slots/{slot.Id}/bookings",
                    new
                    {
                        renterEmail = $"renter{renterNumber}@example.com",
                        partySize = 1
                    });
            })
            .ToArray();

        startGate.SetResult();
        var responses = await Task.WhenAll(bookingTasks);

        var createdCount = responses.Count(response => response.StatusCode == HttpStatusCode.Created);
        var conflictCount = responses.Count(response => response.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(slotCapacity, createdCount);
        Assert.Equal(concurrentRenterCount - slotCapacity, conflictCount);
        Assert.All(
            responses,
            response => Assert.True(
                response.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
                $"Unexpected response status: {(int)response.StatusCode}."));

        using var scope = _factory.Services.CreateScope();
        var slotRepository = scope.ServiceProvider.GetRequiredService<ISlotRepository>();
        var persistedSlot = await slotRepository.GetByIdAsync(slot.Id, CancellationToken.None);

        Assert.NotNull(persistedSlot);
        Assert.Equal(slotCapacity, persistedSlot.BookedSeats);
        Assert.Equal(0, persistedSlot.Capacity - persistedSlot.BookedSeats);
    }

    [Fact]
    public async Task SameRenter_CannotCreateTwoActiveBookingsForOneSlot()
    {
        using var client = _factory.CreateClient();
        var slot = await CreateSlotAsync(client, capacity: 6);

        var first = await client.PostAsJsonAsync($"/slots/{slot.Id}/bookings", new { renterEmail = "same@example.com", partySize = 1 });
        var second = await client.PostAsJsonAsync($"/slots/{slot.Id}/bookings", new { renterEmail = "same@example.com", partySize = 1 });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task OwnerCannotMarkAttendedBeforeSlotStarts()
    {
        using var client = _factory.CreateClient();
        var slot = await CreateSlotAsync(client, capacity: 3);
        var bookingResponse = await client.PostAsJsonAsync($"/slots/{slot.Id}/bookings", new { renterEmail = "future@example.com", partySize = 1 });
        bookingResponse.EnsureSuccessStatusCode();
        var booking = await bookingResponse.Content.ReadFromJsonAsync<BookingResponse>();
        Assert.NotNull(booking);

        var transition = await client.PostAsJsonAsync($"/bookings/{booking.Id}/transitions", new { to = "Attended", actor = "owner", reason = "Early mark" });
        Assert.Equal(HttpStatusCode.Conflict, transition.StatusCode);
    }




    [Fact]
    public async Task OwnerCanCancelConfirmedBookingBeforeSlotStarts()
    {
        using var client = _factory.CreateClient();
        var slot = await CreateSlotAsync(client, capacity: 2);
        var booking = await CreateBookingAsync(client, slot.Id, "owner-cancel@example.com");

        var response = await client.PostAsJsonAsync(
            $"/bookings/{booking.Id}/transitions",
            new { to = "Cancelled", actor = "owner", reason = "Owner rescheduled" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var historyResponse = await client.GetAsync($"/bookings/{booking.Id}/history");
        historyResponse.EnsureSuccessStatusCode();
        var history = await historyResponse.Content.ReadFromJsonAsync<IReadOnlyCollection<BookingHistoryResponse>>();

        Assert.NotNull(history);
        Assert.Contains(history, item => item.To == "Cancelled" && item.Actor == "owner" && item.Reason == "Owner rescheduled");
    }

    [Fact]
    public async Task OwnerCanMarkBookingAttendedAfterSlotStarts()
    {
        using var client = _factory.CreateClient();
        var slot = await CreateSlotAsync(client, capacity: 2);
        var booking = await CreateBookingAsync(client, slot.Id, "attended@example.com");
        await SetSlotStartTimeAsync(slot.Id, DateTimeOffset.UtcNow.AddMinutes(-1));

        var response = await client.PostAsJsonAsync(
            $"/bookings/{booking.Id}/transitions",
            new { to = "Attended", actor = "owner", reason = "Renter attended" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertHistoryEntryAsync(client, booking.Id, "Attended", "owner", "Renter attended");
    }

    [Fact]
    public async Task OwnerCanMarkBookingNoShowAfterSlotStarts()
    {
        using var client = _factory.CreateClient();
        var slot = await CreateSlotAsync(client, capacity: 2);
        var booking = await CreateBookingAsync(client, slot.Id, "noshow@example.com");
        await SetSlotStartTimeAsync(slot.Id, DateTimeOffset.UtcNow.AddMinutes(-1));

        var response = await client.PostAsJsonAsync(
            $"/bookings/{booking.Id}/transitions",
            new { to = "NoShow", actor = "owner", reason = "Renter did not attend" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertHistoryEntryAsync(client, booking.Id, "NoShow", "owner", "Renter did not attend");
    }

    [Fact]
    public async Task RenterCannotMarkBookingAttendedAfterSlotStarts()
    {
        using var client = _factory.CreateClient();
        var slot = await CreateSlotAsync(client, capacity: 2);
        var booking = await CreateBookingAsync(client, slot.Id, "renter-attended@example.com");
        await SetSlotStartTimeAsync(slot.Id, DateTimeOffset.UtcNow.AddMinutes(-1));

        var response = await client.PostAsJsonAsync(
            $"/bookings/{booking.Id}/transitions",
            new { to = "Attended", actor = "renter", reason = "Not authorized" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CancelledBookingCannotTransitionAgain()
    {
        using var client = _factory.CreateClient();
        var slot = await CreateSlotAsync(client, capacity: 2);
        var booking = await CreateBookingAsync(client, slot.Id, "final-state@example.com");

        var cancellation = await client.PostAsJsonAsync(
            $"/bookings/{booking.Id}/transitions",
            new { to = "Cancelled", actor = "renter", reason = "Plans changed" });
        Assert.Equal(HttpStatusCode.OK, cancellation.StatusCode);

        var secondTransition = await client.PostAsJsonAsync(
            $"/bookings/{booking.Id}/transitions",
            new { to = "Cancelled", actor = "owner", reason = "Try again" });

        Assert.Equal(HttpStatusCode.Conflict, secondTransition.StatusCode);
    }

    [Fact]
    public async Task BookingHistoryIncludesInitialAndTransitionEvents()
    {
        using var client = _factory.CreateClient();
        var slot = await CreateSlotAsync(client, capacity: 2);
        var booking = await CreateBookingAsync(client, slot.Id, "history@example.com");

        var cancellation = await client.PostAsJsonAsync(
            $"/bookings/{booking.Id}/transitions",
            new { to = "Cancelled", actor = "renter", reason = "History verification" });
        Assert.Equal(HttpStatusCode.OK, cancellation.StatusCode);

        var response = await client.GetAsync($"/bookings/{booking.Id}/history");
        response.EnsureSuccessStatusCode();
        var history = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<BookingHistoryResponse>>();

        Assert.NotNull(history);
        Assert.Equal(2, history.Count);
        Assert.Contains(history, item => item.From == "Confirmed" && item.To == "Confirmed" && item.Actor == "renter");
        Assert.Contains(history, item =>
            item.From == "Confirmed" &&
            item.To == "Cancelled" &&
            item.Actor == "renter" &&
            item.Reason == "History verification" &&
            item.ChangedAt != default);
    }

    [Fact]
    public async Task BookingRepository_TranslatesUniqueConstraintViolationToDuplicateBooking()
    {
        using var client = _factory.CreateClient();
        var slot = await CreateSlotAsync(client, capacity: 5);

        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();

        var firstBooking = new Booking
        {
            Id = Guid.NewGuid(),
            SlotId = slot.Id,
            RenterEmail = "duplicate@example.com",
            PartySize = 1,
            Status = BookingStatus.Confirmed,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await repository.AddAsync(firstBooking, CancellationToken.None);

        var duplicateBooking = new Booking
        {
            Id = Guid.NewGuid(),
            SlotId = slot.Id,
            RenterEmail = firstBooking.RenterEmail,
            PartySize = 1,
            Status = BookingStatus.Confirmed,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await Assert.ThrowsAsync<DuplicateActiveBookingException>(
            () => repository.AddAsync(duplicateBooking, CancellationToken.None));
    }

    [Fact]
    public async Task BookingRepository_DoesNotTranslateForeignKeyViolationToDuplicateBooking()
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            SlotId = Guid.NewGuid(),
            RenterEmail = "missing-slot@example.com",
            PartySize = 1,
            Status = BookingStatus.Confirmed,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await Assert.ThrowsAsync<DbUpdateException>(
            () => repository.AddAsync(booking, CancellationToken.None));
    }



    [Fact]
    public async Task RenterCannotCancelBookingAfterSlotStarts()
    {
        using var client = _factory.CreateClient();
        var slot = await CreateSlotAsync(client, capacity: 2);
        var booking = await CreateBookingAsync(client, slot.Id, "late-renter-cancel@example.com");
        await SetSlotStartTimeAsync(slot.Id, DateTimeOffset.UtcNow.AddMinutes(-1));

        var response = await client.PostAsJsonAsync(
            $"/bookings/{booking.Id}/transitions",
            new { to = "Cancelled", actor = "renter", reason = "Too late" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task OwnerCannotCancelBookingAfterSlotStarts()
    {
        using var client = _factory.CreateClient();
        var slot = await CreateSlotAsync(client, capacity: 2);
        var booking = await CreateBookingAsync(client, slot.Id, "late-owner-cancel@example.com");
        await SetSlotStartTimeAsync(slot.Id, DateTimeOffset.UtcNow.AddMinutes(-1));

        var response = await client.PostAsJsonAsync(
            $"/bookings/{booking.Id}/transitions",
            new { to = "Cancelled", actor = "owner", reason = "Too late" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RenterCannotMarkBookingNoShowAfterSlotStarts()
    {
        using var client = _factory.CreateClient();
        var slot = await CreateSlotAsync(client, capacity: 2);
        var booking = await CreateBookingAsync(client, slot.Id, "renter-noshow@example.com");
        await SetSlotStartTimeAsync(slot.Id, DateTimeOffset.UtcNow.AddMinutes(-1));

        var response = await client.PostAsJsonAsync(
            $"/bookings/{booking.Id}/transitions",
            new { to = "NoShow", actor = "renter", reason = "Not authorized" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("Attended")]
    [InlineData("NoShow")]
    public async Task FinalBookingStatesCannotTransitionAgain(string finalStatus)
    {
        using var client = _factory.CreateClient();
        var slot = await CreateSlotAsync(client, capacity: 2);
        var booking = await CreateBookingAsync(client, slot.Id, $"final-{finalStatus.ToLowerInvariant()}@example.com");
        await SetSlotStartTimeAsync(slot.Id, DateTimeOffset.UtcNow.AddMinutes(-1));

        var firstTransition = await client.PostAsJsonAsync(
            $"/bookings/{booking.Id}/transitions",
            new { to = finalStatus, actor = "owner", reason = "Final state" });
        Assert.Equal(HttpStatusCode.OK, firstTransition.StatusCode);

        var secondTransition = await client.PostAsJsonAsync(
            $"/bookings/{booking.Id}/transitions",
            new { to = "Cancelled", actor = "owner", reason = "Must be rejected" });

        Assert.Equal(HttpStatusCode.Conflict, secondTransition.StatusCode);
    }

    [Fact]
    public async Task BookingEndpointsReturnNotFoundForMissingResources()
    {
        using var client = _factory.CreateClient();
        var missingId = Guid.NewGuid();

        var bookingResponse = await client.PostAsJsonAsync(
            $"/slots/{missingId}/bookings",
            new { renterEmail = "missing-slot@example.com", partySize = 1 });
        var transitionResponse = await client.PostAsJsonAsync(
            $"/bookings/{missingId}/transitions",
            new { to = "Cancelled", actor = "renter", reason = "Missing booking" });
        var historyResponse = await client.GetAsync($"/bookings/{missingId}/history");

        Assert.Equal(HttpStatusCode.NotFound, bookingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, transitionResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, historyResponse.StatusCode);
    }

    [Fact]
    public async Task BookingAndTransitionEndpointsRejectInvalidInput()
    {
        using var client = _factory.CreateClient();
        var slot = await CreateSlotAsync(client, capacity: 3);

        var invalidBooking = await client.PostAsJsonAsync(
            $"/slots/{slot.Id}/bookings",
            new { renterEmail = "not-an-email", partySize = 7 });
        var booking = await CreateBookingAsync(client, slot.Id, "valid-input@example.com");
        var invalidTransition = await client.PostAsJsonAsync(
            $"/bookings/{booking.Id}/transitions",
            new { to = "UnknownState", actor = "guest", reason = "Invalid request" });

        Assert.Equal(HttpStatusCode.BadRequest, invalidBooking.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidTransition.StatusCode);
    }

    [Fact]
    public async Task SlotListFiltersOrdersAndReturnsRemainingSeats()
    {
        using var client = _factory.CreateClient();
        var propertyId = Guid.NewGuid();
        var otherPropertyId = Guid.NewGuid();
        var laterStart = DateTimeOffset.UtcNow.AddDays(3);
        var earlierStart = DateTimeOffset.UtcNow.AddDays(2);

        async Task<Guid> CreateSlotForPropertyAsync(Guid property, DateTimeOffset startsAt, int capacity)
        {
            var response = await client.PostAsJsonAsync("/slots", new
            {
                propertyId = property,
                startsAt,
                durationMinutes = 30,
                capacity
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<SlotResponse>();
            Assert.NotNull(created);
            return created.Id;
        }

        var laterSlotId = await CreateSlotForPropertyAsync(propertyId, laterStart, 4);
        var earlierSlotId = await CreateSlotForPropertyAsync(propertyId, earlierStart, 5);
        await CreateSlotForPropertyAsync(otherPropertyId, earlierStart, 8);

        var bookingResponse = await client.PostAsJsonAsync(
            $"/slots/{earlierSlotId}/bookings",
            new { renterEmail = "slot-list@example.com", partySize = 2 });
        Assert.Equal(HttpStatusCode.Created, bookingResponse.StatusCode);

        var response = await client.GetAsync(
            $"/slots?propertyId={propertyId}&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedResponse<SlotResponse>>();

        Assert.NotNull(page);
        Assert.Equal(2, page.TotalCount);
        var orderedItems = page.Items.ToArray();
        Assert.Equal(earlierSlotId, orderedItems[0].Id);
        Assert.Equal(laterSlotId, orderedItems[1].Id);
        Assert.Equal(3, orderedItems[0].SeatsRemaining);
        Assert.Equal(4, orderedItems[1].SeatsRemaining);
    }

    [Fact]
    public async Task SlotListRejectsPageSizeAboveMaximum()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/slots?propertyId={Guid.NewGuid()}&page=1&pageSize=101");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }


    [Fact]
    public async Task SlotListWithVeryLargePageNumberReturnsEmptyPageWithoutOverflow()
    {
        using var client = _factory.CreateClient();
        var propertyId = Guid.NewGuid();

        var createResponse = await client.PostAsJsonAsync("/slots", new
        {
            propertyId,
            startsAt = DateTimeOffset.UtcNow.AddDays(1),
            durationMinutes = 30,
            capacity = 5
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var response = await client.GetAsync(
            $"/slots?propertyId={propertyId}&page={int.MaxValue}&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedResponse<SlotResponse>>();

        Assert.NotNull(page);
        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(int.MaxValue, page.Page);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SlotListRejectsNonPositivePageNumber(int pageNumber)
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/slots?propertyId={Guid.NewGuid()}&page={pageNumber}&pageSize=20");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<BookingResponse> CreateBookingAsync(HttpClient client, Guid slotId, string renterEmail)
    {
        var response = await client.PostAsJsonAsync(
            $"/slots/{slotId}/bookings",
            new { renterEmail, partySize = 1 });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BookingResponse>())!;
    }

    private async Task SetSlotStartTimeAsync(Guid slotId, DateTimeOffset startsAt)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var slot = await dbContext.Slots.SingleAsync(item => item.Id == slotId);
        slot.StartsAt = startsAt;
        await dbContext.SaveChangesAsync();
    }

    private static async Task AssertHistoryEntryAsync(
        HttpClient client,
        Guid bookingId,
        string expectedStatus,
        string expectedActor,
        string expectedReason)
    {
        var response = await client.GetAsync($"/bookings/{bookingId}/history");
        response.EnsureSuccessStatusCode();
        var history = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<BookingHistoryResponse>>();

        Assert.NotNull(history);
        Assert.Contains(history, item =>
            item.To == expectedStatus &&
            item.Actor == expectedActor &&
            item.Reason == expectedReason &&
            item.ChangedAt != default);
    }

    private static async Task<SlotResponse> CreateSlotAsync(HttpClient client, int capacity)
    {
        var response = await client.PostAsJsonAsync("/slots", new
        {
            propertyId = Guid.NewGuid(),
            startsAt = DateTimeOffset.UtcNow.AddDays(1),
            durationMinutes = 30,
            capacity
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SlotResponse>())!;
    }

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
