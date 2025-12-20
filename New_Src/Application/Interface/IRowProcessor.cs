namespace Application.Services;

/// <summary>
/// Defines the contract for a component capable of processing individual data rows and accumulating results.
/// Implementations must be stateless and thread-safe, operating solely on the provided accumulator dictionary.
/// </summary>
public interface IRowProcessor
{
    /// <summary>
    /// Processes a single row of project data and updates the provided accumulator with the results.
    /// </summary>
    /// <param name="row">A dictionary representing the current row's data (key-value pairs).</param>
    /// <param name="accumulator">A mutable dictionary to store or aggregate the processing results.</param>
    void ProcessRow(Dictionary<string, object?> row, Dictionary<string, object?> accumulator);

    /// <summary>
    /// Performs any necessary finalization or calculation on the accumulated data after all rows have been processed.
    /// </summary>
    /// <param name="accumulator">The dictionary containing the aggregated data to be finalized.</param>
    void Finalize(Dictionary<string, object?> accumulator);
}