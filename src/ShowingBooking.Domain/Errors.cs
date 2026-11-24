namespace ShowingBooking.Domain;

/// <summary>Every expected failure, with its stable error code.</summary>
public static class Errors
{
    public static Error Validation(string message) =>
        new("validation_failed", message, ErrorKind.Validation);

    public static readonly Error SlotNotFound =
        new("slot_not_found", "The slot does not exist.", ErrorKind.NotFound);

    public static readonly Error BookingNotFound =
        new("booking_not_found", "The booking does not exist.", ErrorKind.NotFound);

    public static readonly Error SlotStarted =
        new("slot_started", "The slot has already started, so it can no longer be booked.", ErrorKind.Conflict);

    public static readonly Error DuplicateActiveBooking =
        new("duplicate_active_booking", "This renter already has an active booking for the slot.", ErrorKind.Conflict);

    public static Error NotEnoughSeats(int requested, int remaining) =>
        new("not_enough_seats",
            $"Party of {requested} does not fit: only {remaining} seat(s) remaining.",
            ErrorKind.Conflict);

    public static Error InvalidTransition(BookingStatus from, BookingStatus to) =>
        new("invalid_transition",
            $"A booking cannot move from {from} to {to}.",
            ErrorKind.Conflict);

    public static readonly Error ActorNotAllowed =
        new("actor_not_allowed", "This actor is not allowed to make that transition.", ErrorKind.Conflict);

    public static readonly Error TooLateToCancel =
        new("too_late_to_cancel", "A booking can only be cancelled before the slot starts.", ErrorKind.Conflict);

    public static readonly Error TooEarlyToComplete =
        new("too_early_to_complete", "Attended or NoShow can only be set once the slot has started.", ErrorKind.Conflict);

    public static readonly Error SlotBusy =
        new("slot_busy", "The slot is being updated by other requests. Please retry.", ErrorKind.Conflict);
}
