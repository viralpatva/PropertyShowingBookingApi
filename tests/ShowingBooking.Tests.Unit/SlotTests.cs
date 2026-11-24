using ShowingBooking.Domain;

namespace ShowingBooking.Tests.Unit;

public class SlotTests
{
    // ---- creating a slot ----

    [Fact]
    public void Create_with_valid_input_succeeds()
    {
        var result = Slot.Create("property-1", TestData.StartsAt, 30, 5, TestData.Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value.SeatsRemaining);
        Assert.Equal(0, result.Value.BookedSeats);
    }

    [Theory]
    [InlineData(0)]   // exactly now is not "in the future"
    [InlineData(-60)] // in the past
    public void Create_rejects_start_that_is_not_in_the_future(int minutesFromNow)
    {
        var result = Slot.Create("p", TestData.Now.AddMinutes(minutesFromNow), 30, 5, TestData.Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(121)]
    public void Create_rejects_duration_out_of_range(int minutes)
    {
        var result = Slot.Create("p", TestData.StartsAt, minutes, 5, TestData.Now);

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(120)]
    public void Create_accepts_duration_boundaries(int minutes)
    {
        Assert.True(Slot.Create("p", TestData.StartsAt, minutes, 5, TestData.Now).IsSuccess);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Create_rejects_capacity_out_of_range(int capacity)
    {
        var result = Slot.Create("p", TestData.StartsAt, 30, capacity, TestData.Now);

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
    }

    // ---- booking seats ----

    [Fact]
    public void Book_uses_seats_equal_to_party_size_and_starts_confirmed()
    {
        var slot = TestData.NewSlot(capacity: 5);

        var result = slot.Book("a@example.com", 3, TestData.Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.Confirmed, result.Value.Status);
        Assert.Equal(3, slot.BookedSeats);
        Assert.Equal(2, slot.SeatsRemaining);
    }

    [Fact]
    public void Book_can_fill_the_slot_exactly()
    {
        var slot = TestData.NewSlot(capacity: 4);

        slot.Book("a@example.com", 2, TestData.Now);
        var result = slot.Book("b@example.com", 2, TestData.Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, slot.SeatsRemaining);
    }

    [Fact]
    public void Book_over_capacity_is_a_conflict_and_changes_nothing()
    {
        var slot = TestData.NewSlot(capacity: 4);
        slot.Book("a@example.com", 3, TestData.Now);
        var versionBefore = slot.Version;

        var result = slot.Book("b@example.com", 2, TestData.Now);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_enough_seats", result.Error!.Code);
        Assert.Equal(ErrorKind.Conflict, result.Error.Kind);
        Assert.Equal(3, slot.BookedSeats);
        Assert.Equal(versionBefore, slot.Version);
        Assert.Single(slot.Bookings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Book_rejects_party_size_out_of_range(int partySize)
    {
        var slot = TestData.NewSlot(capacity: 20);

        var result = slot.Book("a@example.com", partySize, TestData.Now);

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Equal(0, slot.BookedSeats);
    }

    [Fact]
    public void Book_rejects_empty_email()
    {
        var result = TestData.NewSlot().Book("   ", 1, TestData.Now);

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
    }

    [Fact]
    public void Book_is_rejected_once_the_slot_has_started()
    {
        var slot = TestData.NewSlot();

        var result = slot.Book("a@example.com", 1, TestData.StartsAt); // exactly at start

        Assert.Equal("slot_started", result.Error!.Code);
    }

    [Fact]
    public void Book_just_before_start_is_allowed()
    {
        var slot = TestData.NewSlot();

        Assert.True(slot.Book("a@example.com", 1, TestData.StartsAt.AddTicks(-1)).IsSuccess);
    }

    // ---- one active booking per renter per slot ----

    [Fact]
    public void Second_active_booking_by_same_renter_is_a_conflict()
    {
        var slot = TestData.NewSlot();
        slot.Book("a@example.com", 1, TestData.Now);

        var result = slot.Book("a@example.com", 1, TestData.Now);

        Assert.Equal("duplicate_active_booking", result.Error!.Code);
        Assert.Equal(1, slot.BookedSeats);
    }

    [Fact]
    public void Duplicate_check_ignores_case_and_surrounding_spaces()
    {
        var slot = TestData.NewSlot();
        slot.Book("a@example.com", 1, TestData.Now);

        var result = slot.Book("  A@Example.COM ", 1, TestData.Now);

        Assert.Equal("duplicate_active_booking", result.Error!.Code);
    }

    [Fact]
    public void Renter_can_book_again_after_cancelling()
    {
        var slot = TestData.NewSlot();
        var first = TestData.NewBooking(slot, "a@example.com", 2);
        first.Transition(BookingStatus.Cancelled, Actor.Renter, null, TestData.Now);

        var result = slot.Book("a@example.com", 2, TestData.Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, slot.BookedSeats);
    }

    [Fact]
    public void Different_renters_can_book_the_same_slot()
    {
        var slot = TestData.NewSlot();

        Assert.True(slot.Book("a@example.com", 1, TestData.Now).IsSuccess);
        Assert.True(slot.Book("b@example.com", 1, TestData.Now).IsSuccess);
    }
}
