namespace PropertyShowing.Domain.Entities;

public sealed class ShowingSlot
{
    public Guid Id { get; set; }
    public Guid PropertyId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public int DurationMinutes { get; set; }
    public int Capacity { get; set; }
    public int BookedSeats { get; set; }
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}
