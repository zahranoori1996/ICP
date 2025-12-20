namespace Api.Controllers;

/// <summary>
/// A standardized response wrapper for API endpoints.
/// </summary>
/// <typeparam name="T">The type of the data payload.</typeparam>
public class ApiResponse<T>
{
    /// <summary>
    /// Gets or sets the data payload.
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the request was successful.
    /// </summary>
    public bool Succeeded { get; set; }

    /// <summary>
    /// Gets or sets the list of messages (e.g., errors or notifications).
    /// </summary>
    public IEnumerable<string> Messages { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiResponse{T}"/> class.
    /// </summary>
    public ApiResponse() 
    { 
        Messages = Array.Empty<string>(); 
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiResponse{T}"/> class with specified values.
    /// </summary>
    /// <param name="succeeded">Indicates if the operation was successful.</param>
    /// <param name="data">The data payload.</param>
    /// <param name="messages">Optional messages associated with the response.</param>
    public ApiResponse(bool succeeded, T? data, IEnumerable<string>? messages = null)
    {
        Succeeded = succeeded;
        Data = data;
        Messages = messages ?? Array.Empty<string>();
    }
}