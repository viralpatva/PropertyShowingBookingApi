namespace PropertyShowing.Application.Abstractions;

public sealed class BookingConcurrencyException(Exception innerException)
    : Exception("The booking was changed by another request.", innerException);
