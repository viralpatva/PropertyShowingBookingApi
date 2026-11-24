namespace ShowingBooking.Domain;

public enum BookingStatus
{
    Confirmed,
    Cancelled,
    Attended,
    NoShow
}

/// <summary>
/// Who is performing an action. There is no authentication in this assignment,
/// so the caller simply states the actor and we trust it (see README, assumption 1).
/// </summary>
public enum Actor
{
    Renter,
    Owner
}
