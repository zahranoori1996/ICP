using Application.DTOs;
using Shared.Wrapper;

namespace Application.Services;

public interface IRowCorrectionService : IBaseCorrectionService
{
    /// <summary>
    /// Analyzes the project data to identify rows that appear to be empty or contain statistical outliers.
    /// </summary>
    Task<Result<List<EmptyRowDto>>> FindEmptyRowsAsync(FindEmptyRowsRequest request);

    /// <summary>
    /// Permanently removes the specified sample rows from the project.
    /// </summary>
    Task<Result<int>> DeleteRowsAsync(DeleteRowsRequest request);

    /// <summary>
    /// Applies specified optimization settings (blank subtraction and scaling) to the project data.
    /// </summary>
    Task<Result<CorrectionResultDto>> ApplyOptimizationAsync(ApplyOptimizationRequest request);
}