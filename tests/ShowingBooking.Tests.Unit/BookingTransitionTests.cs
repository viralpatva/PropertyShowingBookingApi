using ShowingBooking.Domain;

namespace ShowingBooking.Tests.Unit;

public class BookingTransitionTests
{
    private static readonly DateTimeOffset BeforeStart = TestData.StartsAt.AddMinutes(-1);
    private static readonly DateTimeOffset AfterStart = TestData.StartsAt.AddMinutes(1);

    // Moves a fresh booking into the given state using only legal transitions.
    private static Booking BookingIn(BookingStatus status)
    {
        var booking = TestData.NewBooking(TestData.NewSlot(), partySize: 2);
        switch (status)
        {
            case BookingStatus.Cancelled:
                booking.Transition(BookingStatus.Cancelled, Actor.Renter, null, BeforeStart);
                break;
            case BookingStatus.Attended:
            case BookingStatus.NoShow:
                booking.Transition(status, Actor.Owner, null, AfterStart);
                break;
        }
        return booking;
    }

    // ---- Confirmed -> Cancelled ----

    [Theory]
    [InlineData(Actor.Renter)]
    [InlineData(Actor.Owner)]
    public void Renter_or_owner_can_cancel_before_start_and_seats_are_freed(Actor actor)
    {
        var slot = TestData.NewSlot(capacity: 4);
        var booking = TestData.NewBooking(slot, partySize: 3);

        var result = booking.Transition(BookingStatus.Cancelled, actor, "plans changed", BeforeStart);

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal(0, slot.BookedSeats);
        Assert.Equal(4, slot.SeatsRemaining);
    }

    [Theory]
    [InlineData(0)] // exactly at startsAt: "only before" is strict
    [InlineData(1)] // after the start
    public void Cancel_at_or_after_start_is_rejected_and_seats_stay_used(int minutesAfterStart)
    {
        var slot = TestData.NewSlot();
        var booking = TestData.NewBooking(slot, partySize: 2);

        var result = booking.Transition(
            BookingStatus.Cancelled, Actor.Renter, null, TestData.StartsAt.AddMinutes(minutesAfterStart));

        Assert.Equal("too_late_to_cancel", result.Error!.Code);
        Assert.Equal(ErrorKind.Conflict, result.Error.Kind);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(2, slot.BookedSeats);
    }

    // ---- Confirmed -> Attended / NoShow ----

    [Theory]
    [InlineData(BookingStatus.Attended, 0)]  // exactly at startsAt is allowed
    [InlineData(BookingStatus.Attended, 1)]
    [InlineData(BookingStatus.NoShow, 0)]
    [InlineData(BookingStatus.NoShow, 1)]
    public void Owner_can_mark_attended_or_noshow_at_or_after_start_and_seats_stay_used(
        BookingStatus to, int minutesAfterStart)
    {
        var slot = TestData.NewSlot();
        var booking = TestData.NewBooking(slot, partySize: 2);

        var result = booking.Transition(to, Actor.Owner, null, TestData.StartsAt.AddMinutes(minutesAfterStart));

        Assert.True(result.IsSuccess);
        Assert.Equal(to, booking.Status);
        Assert.Equal(2, slot.BookedSeats);
    }

    [Theory]
    [InlineData(BookingStatus.Attended)]
    [InlineData(BookingStatus.NoShow)]
    public void Renter_cannot_mark_attended_or_noshow(BookingStatus to)
    {
        var booking = TestData.NewBooking(TestData.NewSlot());

        var result = booking.Transition(to, Actor.Renter, null, AfterStart);

        Assert.Equal("actor_not_allowed", result.Error!.Code);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    [Theory]
    [InlineData(BookingStatus.Attended)]
    [InlineData(BookingStatus.NoShow)]
    public void Owner_cannot_mark_attended_or_noshow_before_start(BookingStatus to)
    {
        var booking = TestData.NewBooking(TestData.NewSlot());

        var result = booking.Transition(to, Actor.Owner, null, BeforeStart);

        Assert.Equal("too_early_to_complete", result.Error!.Code);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    // ---- final states and nonsense targets ----

    public static IEnumerable<object[]> FinalStateToAnyState()
    {
        foreach (var finalState in new[] { BookingStatus.Cancelled, BookingStatus.Attended, BookingStatus.NoShow })
            foreach (var target in Enum.GetValues<BookingStatus>())
                yield return new object[] { finalState, target };
    }

    [Theory]
    [MemberData(nameof(FinalStateToAnyState))]
    public void Final_states_cannot_change(BookingStatus finalState, BookingStatus target)
    {
        var booking = BookingIn(finalState);
        var historyCount = booking.History.Count;

        var result = booking.Transition(target, Actor.Owner, null, AfterStart);

        Assert.Equal("invalid_transition", result.Error!.Code);
        Assert.Equal(finalState, booking.Status);
        Assert.Equal(historyCount, booking.History.Count);
    }

    [Fact]
    public void Cancelling_twice_does_not_free_seats_twice()
    {
        var slot = TestData.NewSlot(capacity: 4);
        TestData.NewBooking(slot, "other@example.com", 1);
        var booking = TestData.NewBooking(slot, "a@example.com", 2);

        booking.Transition(BookingStatus.Cancelled, Actor.Renter, null, BeforeStart);
        booking.Transition(BookingStatus.Cancelled, Actor.Owner, null, BeforeStart);

        Assert.Equal(1, slot.BookedSeats);
    }

    [Fact]
    public void Confirmed_to_confirmed_is_not_a_transition()
    {
        var booking = TestData.NewBooking(TestData.NewSlot());

        var result = booking.Transition(BookingStatus.Confirmed, Actor.Owner, null, BeforeStart);

        Assert.Equal("invalid_transition", result.Error!.Code);
    }

    // ---- history ----

    [Fact]
    public void New_booking_has_one_history_entry_for_its_creation()
    {
        var booking = TestData.NewBooking(TestData.NewSlot());

        var entry = Assert.Single(booking.History);
        Assert.Null(entry.FromStatus);
        Assert.Equal(BookingStatus.Confirmed, entry.ToStatus);
        Assert.Equal(Actor.Renter, entry.Actor);
        Assert.Equal(TestData.Now, entry.OccurredAt);
    }

    [Fact]
    public void Transition_appends_a_history_entry_with_actor_reason_and_time()
    {
        var booking = TestData.NewBooking(TestData.NewSlot());

        booking.Transition(BookingStatus.Cancelled, Actor.Owner, "  owner unavailable ", BeforeStart);

        var last = booking.History.Last();
        Assert.Equal(2, booking.History.Count);
        Assert.Equal(BookingStatus.Confirmed, last.FromStatus);
        Assert.Equal(BookingStatus.Cancelled, last.ToStatus);
        Assert.Equal(Actor.Owner, last.Actor);
        Assert.Equal("owner unavailable", last.Reason);
        Assert.Equal(BeforeStart, last.OccurredAt);
    }

    [Fact]
    public void Rejected_transition_writes_no_history()
    {
        var booking = TestData.NewBooking(TestData.NewSlot());

        booking.Transition(BookingStatus.Attended, Actor.Renter, null, AfterStart);

        Assert.Single(booking.History);
    }
}
