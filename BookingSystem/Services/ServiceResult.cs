namespace BookingSystem.Services;

/// <summary>
/// Represents the outcome of a service operation without throwing exceptions
/// for expected failures (validation, business rules, not-found, etc.).
/// </summary>
public sealed class ServiceResult<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string Error { get; }

    private ServiceResult(bool success, T? value, string error)
    {
        IsSuccess = success;
        Value = value;
        Error = error;
    }

    public static ServiceResult<T> Ok(T value) => new(true,  value, string.Empty);
    public static ServiceResult<T> Fail(string error) => new(false, default, error);

    /// <summary>Project to a different value type, preserving failure state.</summary>
    public ServiceResult<TOut> Map<TOut>(Func<T, TOut> mapper) =>
        IsSuccess
            ? ServiceResult<TOut>.Ok(mapper(Value!))
            : ServiceResult<TOut>.Fail(Error);
}

/// <summary>Non-generic variant for operations that return no value.</summary>
public sealed class ServiceResult
{
    public bool IsSuccess { get; }
    public string Error { get; }

    private ServiceResult(bool success, string error)
    {
        IsSuccess = success;
        Error = error;
    }

    public static ServiceResult Ok() => new(true, string.Empty);
    public static ServiceResult Fail(string error) => new(false, error);
}
