using Application.DTOs;
using Application.Services;
using Shared.Wrapper;

namespace Application.Interface;

public interface IDfCorrectionService : IBaseCorrectionService
{
    /// <summary>
    /// Retrieves a list of all samples in a project along with their currently assigned dilution factors.
    /// </summary>
    Task<Result<List<DfSampleDto>>> GetDfSamplesAsync(Guid projectId);

    /// <summary>
    /// Updates the dilution factor (DF) for a specified list of samples and adjusts the corrected concentrations accordingly.
    /// </summary>
    Task<Result<CorrectionResultDto>> ApplyDfCorrectionAsync(DfCorrectionRequest request);
}