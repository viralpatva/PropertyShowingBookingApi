namespace PropertyShowing.Api.Entities;
public sealed class Booking { public Guid Id { get; set; } public Guid SlotId { get; set; } public ShowingSlot Slot { get; set; } = null!; public string RenterEmail { get; set; } = string.Empty; public int PartySize { get; set; } public BookingStatus Status { get; set; } = BookingStatus.Confirmed; public DateTimeOffset CreatedAt { get; set; } public ICollection<BookingHistory> History { get; set; } = new List<BookingHistory>(); }
public enum BookingStatus { Confirmed, Cancelled, Attended, NoShow }
