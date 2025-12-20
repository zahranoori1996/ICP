namespace Shared.Wrapper;

/// <summary>
/// Represents a non-generic result of an operation, indicating success or failure.
/// </summary>
public class Result
{
    /// <summary>
    /// Gets or sets a value indicating whether the operation succeeded.
    /// </summary>
    public bool Succeeded { get; set; }

    /// <summary>
    /// Gets or sets the collection of result messages (e.g., error messages).
    /// </summary>
    public string[] Messages { get; set; } = Array.Empty<string>();

    internal Result(bool succeeded, IEnumerable<string> messages)
    {
        Succeeded = succeeded;
        Messages = messages as string[] ?? messages.ToArray();
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <returns>A successful Result.</returns>
    public static Result Success() => new Result(true, Array.Empty<string>());

    /// <summary>
    /// Creates a failed result with a specific message.
    /// </summary>
    /// <param name="message">The failure message.</param>
    /// <returns>A failed Result.</returns>
    public static Result Fail(string message) => new Result(false, new[] { message });
}

/// <summary>
/// Represents a generic result of an operation containing data.
/// </summary>
/// <typeparam name="T">The type of the data.</typeparam>
public class Result<T> : Result
{
    /// <summary>
    /// Gets or sets the data resulting from the operation.
    /// </summary>
    public T? Data { get; set; }

    internal Result(bool succeeded, T? data, IEnumerable<string> messages) : base(succeeded, messages)
    {
        Data = data;
    }

    /// <summary>
    /// Creates a successful result with data.
    /// </summary>
    /// <param name="data">The data to return.</param>
    /// <returns>A successful Result containing the data.</returns>
    public static Result<T> Success(T data) => new Result<T>(true, data, Array.Empty<string>());

    /// <summary>
    /// Creates a failed result with a specific message.
    /// </summary>
    /// <param name="message">The failure message.</param>
    /// <returns>A failed Result.</returns>
    public static new Result<T> Fail(string message) => new Result<T>(false, default, new[] { message });
}

public record ElementStats(int Passed, decimal MeanDiff, decimal MinDiff, decimal MaxDiff, decimal Variance);
