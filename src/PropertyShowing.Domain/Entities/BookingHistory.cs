namespace PropertyShowing.Domain.Entities;

public sealed class BookingHistory
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public BookingStatus FromStatus { get; set; }
    public BookingStatus ToStatus { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
