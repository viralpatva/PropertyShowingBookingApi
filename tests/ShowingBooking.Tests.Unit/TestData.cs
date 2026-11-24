using ShowingBooking.Domain;

namespace ShowingBooking.Tests.Unit;

internal static class TestData
{
    // Fixed clock values so every test is deterministic.
    public static readonly DateTimeOffset Now = new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset StartsAt = Now.AddDays(1);

    public static Slot NewSlot(int capacity = 4) =>
        Slot.Create("property-1", StartsAt, 30, capacity, Now).Value;

    public static Booking NewBooking(Slot slot, string email = "renter@example.com", int partySize = 1) =>
        slot.Book(email, partySize, Now).Value;
}
