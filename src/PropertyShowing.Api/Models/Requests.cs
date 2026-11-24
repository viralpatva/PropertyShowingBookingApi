using System.ComponentModel.DataAnnotations;
namespace PropertyShowing.Api.Models;
public sealed record CreateSlotRequest(Guid PropertyId, DateTimeOffset StartsAt, int DurationMinutes, int Capacity) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PropertyId == Guid.Empty) yield return new ValidationResult("propertyId is required.", [nameof(PropertyId)]);
        if (StartsAt <= DateTimeOffset.UtcNow) yield return new ValidationResult("startsAt must be in the future.", [nameof(StartsAt)]);
        if (DurationMinutes is < 15 or > 120) yield return new ValidationResult("durationMinutes must be between 15 and 120.", [nameof(DurationMinutes)]);
        if (Capacity is < 1 or > 20) yield return new ValidationResult("capacity must be between 1 and 20.", [nameof(Capacity)]);
    }
}
public sealed record CreateBookingRequest(string RenterEmail, int PartySize) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(RenterEmail) || RenterEmail.Length > 254 || !MailAddress.TryCreate(RenterEmail, out _)) yield return new ValidationResult("A valid renterEmail is required.", [nameof(RenterEmail)]);
        if (PartySize is < 1 or > 6) yield return new ValidationResult("partySize must be between 1 and 6.", [nameof(PartySize)]);
    }
}
public sealed record TransitionBookingRequest(string To, string Actor, string? Reason) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enum.TryParse<BookingStatus>(To, true, out var status) || status == BookingStatus.Confirmed) yield return new ValidationResult("to must be Cancelled, Attended or NoShow.", [nameof(To)]);
        if (Actor is not ("owner" or "renter")) yield return new ValidationResult("actor must be owner or renter.", [nameof(Actor)]);
        if (Reason?.Length > 500) yield return new ValidationResult("reason cannot exceed 500 characters.", [nameof(Reason)]);
    }
}
public sealed record SlotQueryRequest(Guid PropertyId, DateTimeOffset? From, DateTimeOffset? To, int Page = 1, int PageSize = 20) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PropertyId == Guid.Empty) yield return new ValidationResult("propertyId is required.", [nameof(PropertyId)]);
        if (From.HasValue && To.HasValue && From > To) yield return new ValidationResult("from must be earlier than or equal to to.", [nameof(From), nameof(To)]);
        if (Page < 1) yield return new ValidationResult("page must be at least 1.", [nameof(Page)]);
        if (PageSize is < 1 or > 100) yield return new ValidationResult("pageSize must be between 1 and 100.", [nameof(PageSize)]);
    }
}
