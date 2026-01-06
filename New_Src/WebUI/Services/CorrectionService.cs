using Application.DTOs; // ✅ استفاده از DTOهای مشترک
using System.Net.Http.Json;
using System.Text.Json;

namespace WebUI.Services;

// ============================================
// Correction Service
// ============================================

public class CorrectionService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CorrectionService> _logger;
    private readonly AuthService _authService;
    private readonly JsonSerializerOptions _jsonOptions;

    public CorrectionService(IHttpClientFactory clientFactory, ILogger<CorrectionService> logger, AuthService authService)
    {
        _httpClient = clientFactory.CreateClient("Api");
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        _logger = logger;
        _authService = authService;
        // ✅ تنظیم برای مپ کردن حروف کوچک/بزرگ (camelCase به PascalCase)
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    /// <summary>
    /// Find samples with bad weights
    /// </summary>
    public async Task<ServiceResult<List<BadSampleDto>>> FindBadWeightsAsync(Guid projectId, decimal min = 0.09m, decimal max = 0.11m)
    {
        try
        {
            SetAuthHeader();
            // استفاده از کلاس درخواست استاندارد بک‌اند
            var request = new FindBadWeightsRequest(projectId, min, max);

            var response = await _httpClient.PostAsJsonAsync("correction/bad-weights", request);
            return await HandleResponseAsync<List<BadSampleDto>>(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding bad weights");
            return ServiceResult<List<BadSampleDto>>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Find samples with bad volumes
    /// </summary>
    public async Task<ServiceResult<List<BadSampleDto>>> FindBadVolumesAsync(Guid projectId, decimal expectedVolume = 10m)
    {
        try
        {
            SetAuthHeader();
            var request = new FindBadVolumesRequest(projectId, expectedVolume);

            var response = await _httpClient.PostAsJsonAsync("correction/bad-volumes", request);
            return await HandleResponseAsync<List<BadSampleDto>>(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding bad volumes");
            return ServiceResult<List<BadSampleDto>>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get all DF samples for a project
    /// </summary>
    public async Task<ServiceResult<List<DfSampleDto>>> GetDfSamplesAsync(Guid projectId)
    {
        try
        {
            SetAuthHeader();
            var response = await _httpClient.GetAsync($"correction/{projectId}/df-samples");
            return await HandleResponseAsync<List<DfSampleDto>>(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting DF samples");
            return ServiceResult<List<DfSampleDto>>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Find empty/outlier rows
    /// </summary>
    public async Task<ServiceResult<List<EmptyRowDto>>> FindEmptyRowsAsync(Guid projectId, decimal thresholdPercent = 70m)
    {
        try
        {
            SetAuthHeader();
            var request = new FindEmptyRowsRequest(projectId, thresholdPercent);

            var response = await _httpClient.PostAsJsonAsync("correction/empty-rows", request);
            return await HandleResponseAsync<List<EmptyRowDto>>(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding empty rows");
            return ServiceResult<List<EmptyRowDto>>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply weight correction
    /// </summary>
    public async Task<ServiceResult<CorrectionResultDto>> ApplyWeightCorrectionAsync(
        Guid projectId, List<string> solutionLabels, decimal newWeight)
    {
        try
        {
            SetAuthHeader();
            var request = new WeightCorrectionRequest(projectId, solutionLabels, newWeight);

            var response = await _httpClient.PostAsJsonAsync("correction/weight", request);
            return await HandleResponseAsync<CorrectionResultDto>(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying weight correction");
            return ServiceResult<CorrectionResultDto>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply volume correction
    /// </summary>
    public async Task<ServiceResult<CorrectionResultDto>> ApplyVolumeCorrectionAsync(
        Guid projectId, List<string> solutionLabels, decimal newVolume)
    {
        try
        {
            SetAuthHeader();
            var request = new VolumeCorrectionRequest(projectId, solutionLabels, newVolume);

            var response = await _httpClient.PostAsJsonAsync("correction/volume", request);
            return await HandleResponseAsync<CorrectionResultDto>(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying volume correction");
            return ServiceResult<CorrectionResultDto>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply DF correction
    /// </summary>
    public async Task<ServiceResult<CorrectionResultDto>> ApplyDfCorrectionAsync(
        Guid projectId, List<string> solutionLabels, decimal newDf)
    {
        try
        {
            SetAuthHeader();
            var request = new DfCorrectionRequest(projectId, solutionLabels, newDf);

            var response = await _httpClient.PostAsJsonAsync("correction/df", request);
            return await HandleResponseAsync<CorrectionResultDto>(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying DF correction");
            return ServiceResult<CorrectionResultDto>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Delete rows by solution labels
    /// </summary>
    public async Task<ServiceResult<int>> DeleteRowsAsync(Guid projectId, List<string> solutionLabels)
    {
        try
        {
            SetAuthHeader();
            var request = new DeleteRowsRequest(projectId, solutionLabels);

            var response = await _httpClient.PostAsJsonAsync("correction/delete-rows", request);
            return await HandleResponseAsync<int>(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting rows");
            return ServiceResult<int>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply Optimization (Added for compatibility)
    /// </summary>
    public async Task<ServiceResult<CorrectionResultDto>> ApplyOptimizationAsync(
        Guid projectId, Dictionary<string, ElementSettings> elementSettings)
    {
        try
        {
            SetAuthHeader();
            var request = new ApplyOptimizationRequest(projectId, elementSettings);

            var response = await _httpClient.PostAsJsonAsync("correction/apply-optimization", request);
            return await HandleResponseAsync<CorrectionResultDto>(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying optimization");
            return ServiceResult<CorrectionResultDto>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Undo last correction (Added for compatibility)
    /// </summary>
    public async Task<ServiceResult<bool>> UndoLastCorrectionAsync(Guid projectId)
    {
        try
        {
            SetAuthHeader();
            var request = new { }; // Empty body
            var response = await _httpClient.PostAsJsonAsync($"correction/{projectId}/undo", request);
            return await HandleResponseAsync<bool>(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error undoing last correction");
            return ServiceResult<bool>.Fail($"Error: {ex.Message}");
        }
    }

    // ============================================
    // Helpers
    // ============================================

    private void SetAuthHeader()
    {
        var token = _authService.GetAccessToken();
        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
    }

    private async Task<ServiceResult<T>> HandleResponseAsync<T>(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            var result = JsonSerializer.Deserialize<ApiResult<T>>(content, _jsonOptions);
            if (result?.Succeeded == true && result.Data != null)
                return ServiceResult<T>.Success(result.Data);

            return ServiceResult<T>.Fail(result?.Message ?? "Failed");
        }

        return ServiceResult<T>.Fail($"Server error: {response.StatusCode}");
    }
}