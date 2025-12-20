using Application.DTOs;
using Shared.Wrapper;

namespace Application.Services;

public interface IVolumeCorrectionService : IBaseCorrectionService
{
    /// <summary>
    /// Scans the project data to identify samples where the recorded volume deviates from the expected standard value.
    /// </summary>
    Task<Result<List<BadSampleDto>>> FindBadVolumesAsync(FindBadVolumesRequest request);

    /// <summary>
    /// Applies corrections to the recorded volume for a specified list of samples and recalculates their concentrations.
    /// </summary>
    Task<Result<CorrectionResultDto>> ApplyVolumeCorrectionAsync(VolumeCorrectionRequest request);
}