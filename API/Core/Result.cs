namespace API.Core;

public class Result<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public string? Error { get; init; }
    public Dictionary<string, string[]>? ValidationErrors { get; init; }
    public FailureReason? Reason { get; init; }

    public static Result<T> Success(T value) => new() { IsSuccess = true, Value = value };
    public static Result<T> Failure(string error, FailureReason reason) =>
        new() { IsSuccess = false, Error = error, Reason = reason };

    public static Result<T> ValidationFailure(string field, string error) =>
        ValidationFailure(new Dictionary<string, string[]> { [field] = [error] });

    public static Result<T> ValidationFailure(Dictionary<string, string[]> validationErrors) =>
        new()
        {
            IsSuccess = false,
            Error = string.Join("; ", validationErrors.Values.SelectMany(errors => errors)),
            ValidationErrors = validationErrors,
            Reason = FailureReason.Validation
        };
}
