using ShowingBooking.Domain;

namespace ShowingBooking.Api;

internal static class ResultHttpExtensions
{
    /// <summary>Success runs the given mapping, failure becomes a ProblemDetails response.</summary>
    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : result.Error!.ToProblem();

    public static IResult ToProblem(this Error error)
    {
        var (status, title) = error.Kind switch
        {
            ErrorKind.Validation => (StatusCodes.Status400BadRequest, "Invalid request"),
            ErrorKind.NotFound => (StatusCodes.Status404NotFound, "Not found"),
            _ => (StatusCodes.Status409Conflict, "Conflict")
        };

        return Results.Problem(
            statusCode: status,
            title: title,
            detail: error.Message,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}
