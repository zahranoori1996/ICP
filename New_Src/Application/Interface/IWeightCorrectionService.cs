using Application.DTOs;
using Shared.Wrapper;

namespace Application.Services;

public interface IWeightCorrectionService : IBaseCorrectionService
{
    /// <summary>
    /// Scans the project data to identify samples where the recorded weight falls outside the specified acceptable range.
    /// </summary>
    Task<Result<List<BadSampleDto>>> FindBadWeightsAsync(FindBadWeightsRequest request);

    /// <summary>
    /// Applies corrections to the recorded weight for a specified list of samples and recalculates their concentrations.
    /// </summary>
    Task<Result<CorrectionResultDto>> ApplyWeightCorrectionAsync(WeightCorrectionRequest request);
}