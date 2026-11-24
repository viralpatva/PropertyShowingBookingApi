using FluentValidation.Results;
using ShowingBooking.Domain;

namespace ShowingBooking.Application.Common;

internal static class ValidationExtensions
{
    public static Error ToError(this ValidationResult validation) =>
        Errors.Validation(string.Join(" ", validation.Errors.Select(e => e.ErrorMessage).Distinct()));

    /// <summary>True only for a real member name such as "Cancelled". Rejects "1" and "Cancelled,NoShow".</summary>
    public static bool IsEnumName<TEnum>(string? value) where TEnum : struct, Enum =>
        !string.IsNullOrWhiteSpace(value) &&
        Enum.GetNames<TEnum>().Any(n => n.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
}
