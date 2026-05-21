namespace DragonOS.AudioConfigurator.Core.Models;

/// <summary>
/// Represents the discriminated outcome of a fallible operation, carrying either a
/// typed success value or a structured error description.
/// </summary>
/// <typeparam name="T">
/// The type of the value produced on success. Use <see cref="Unit"/> for operations
/// that succeed without producing a meaningful value.
/// </typeparam>
/// <remarks>
/// <para>
/// This type deliberately avoids using raw exceptions for control flow across service
/// boundaries. Exceptions are still captured and stored for diagnostic purposes, but
/// callers are expected to branch on <see cref="IsSuccess"/> rather than catching.
/// </para>
/// <para>
/// <b>Usage pattern:</b>
/// <code>
/// var result = await audioRouter.RouteApplicationAsync(...);
/// if (!result.IsSuccess)
/// {
///     logger.LogError(result.Exception, result.ErrorMessage);
///     return;
/// }
/// // result.Value is guaranteed non-null here.
/// </code>
/// </para>
/// </remarks>
public sealed class OperationResult<T>
{
    // -------------------------------------------------------------------------
    // Properties
    // -------------------------------------------------------------------------

    /// <summary>
    /// <see langword="true"/> if the operation completed without error;
    /// <see langword="false"/> otherwise.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// The value produced by a successful operation.
    /// Always <see langword="null"/> when <see cref="IsSuccess"/> is <see langword="false"/>.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// A human-readable description of the failure.
    /// Always <see langword="null"/> when <see cref="IsSuccess"/> is <see langword="true"/>.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// The underlying exception that caused the failure, if one was captured.
    /// May be <see langword="null"/> even on failure if the error was a logic condition
    /// rather than an exception.
    /// </summary>
    public Exception? Exception { get; }

    // -------------------------------------------------------------------------
    // Private constructors — callers use the static factory methods below.
    // -------------------------------------------------------------------------

    private OperationResult(T value)
    {
        IsSuccess = true;
        Value = value;
    }

    private OperationResult(string errorMessage, Exception? exception)
    {
        IsSuccess = false;
        ErrorMessage = errorMessage;
        Exception = exception;
    }

    // -------------------------------------------------------------------------
    // Static factory methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a successful result wrapping <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The value to wrap.</param>
    public static OperationResult<T> Success(T value) => new(value);

    /// <summary>
    /// Creates a failed result with a descriptive <paramref name="errorMessage"/>.
    /// </summary>
    /// <param name="errorMessage">A message describing what went wrong.</param>
    /// <param name="exception">
    /// The exception that caused the failure, if applicable.
    /// </param>
    public static OperationResult<T> Failure(string errorMessage, Exception? exception = null)
        => new(errorMessage, exception);

    /// <inheritdoc/>
    public override string ToString()
        => IsSuccess
            ? $"Success({Value})"
            : $"Failure({ErrorMessage})";
}
