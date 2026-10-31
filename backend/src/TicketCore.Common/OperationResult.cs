namespace TicketCore.Common;

/// <summary>Kinds of failure a service can report; the API maps each one to an HTTP status.</summary>
public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized
}

public sealed record OperationError(ErrorKind Kind, string Code, string Message);

public class OperationResult
{
    protected OperationResult(OperationError? error) => Error = error;

    public OperationError? Error { get; }
    public bool Succeeded => Error is null;

    public static OperationResult Ok() => new(null);
    public static OperationResult Fail(ErrorKind kind, string code, string message) => new(new OperationError(kind, code, message));
}

public sealed class OperationResult<T> : OperationResult
{
    private OperationResult(T? value, OperationError? error) : base(error) => Value = value;

    public T? Value { get; }

    public static OperationResult<T> Ok(T value) => new(value, null);
    public static new OperationResult<T> Fail(ErrorKind kind, string code, string message) => new(default, new OperationError(kind, code, message));
}
