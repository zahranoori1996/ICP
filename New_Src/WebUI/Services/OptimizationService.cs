using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebUI.Services;

// ============================================
// Optimization DTOs
// ============================================

public class BlankScaleOptimizationRequest
{
    [JsonPropertyName("projectId")]
    public Guid ProjectId { get; set; }

    [JsonPropertyName("elements")]
    public List<string>? Elements { get; set; }

    [JsonPropertyName("minDiffPercent")]
    public decimal MinDiffPercent { get; set; } = -10m;

    [JsonPropertyName("maxDiffPercent")]
    public decimal MaxDiffPercent { get; set; } = 10m;

    [JsonPropertyName("maxIterations")]
    public int MaxIterations { get; set; } = 100;

    [JsonPropertyName("populationSize")]
    public int PopulationSize { get; set; } = 50;

    [JsonPropertyName("useMultiModel")]
    public bool UseMultiModel { get; set; } = true;

    [JsonPropertyName("seed")]
    public int? Seed { get; set; }
}

public class BlankScaleOptimizationResult
{
    [JsonPropertyName("totalRmSamples")]
    public int TotalRmSamples { get; set; }

    [JsonPropertyName("passedBefore")]
    public int PassedBefore { get; set; }

    [JsonPropertyName("passedAfter")]
    public int PassedAfter { get; set; }

    [JsonPropertyName("improvementPercent")]
    public decimal ImprovementPercent { get; set; }

    [JsonPropertyName("elementOptimizations")]
    public Dictionary<string, ElementOptimization> ElementOptimizations { get; set; } = new();

    [JsonPropertyName("optimizedData")]
    public List<OptimizedSampleDto> OptimizedData { get; set; } = new();

    [JsonPropertyName("modelSummary")]
    public MultiModelSummary? ModelSummary { get; set; }
}

public class ElementOptimization
{
    [JsonPropertyName("element")]
    public string Element { get; set; } = "";

    [JsonPropertyName("blank")]
    public decimal Blank { get; set; }

    [JsonPropertyName("scale")]
    public decimal Scale { get; set; }

    [JsonPropertyName("passedBefore")]
    public int PassedBefore { get; set; }

    [JsonPropertyName("passedAfter")]
    public int PassedAfter { get; set; }

    [JsonPropertyName("meanDiffBefore")]
    public decimal MeanDiffBefore { get; set; }

    [JsonPropertyName("meanDiffAfter")]
    public decimal MeanDiffAfter { get; set; }

    [JsonPropertyName("selectedModel")]
    public string SelectedModel { get; set; } = "A";
}

public class OptimizedSampleDto
{
    [JsonPropertyName("solutionLabel")]
    public string SolutionLabel { get; set; } = "";

    [JsonPropertyName("crmId")]
    public string CrmId { get; set; } = "";

    [JsonPropertyName("element")]
    public string Element { get; set; } = "";

    [JsonPropertyName("originalValue")]
    public decimal OriginalValue { get; set; }

    [JsonPropertyName("optimizedValue")]
    public decimal OptimizedValue { get; set; }

    [JsonPropertyName("crmValue")]
    public decimal CrmValue { get; set; }

    [JsonPropertyName("originalDiff")]
    public decimal OriginalDiff { get; set; }

    [JsonPropertyName("optimizedDiff")]
    public decimal OptimizedDiff { get; set; }

    [JsonPropertyName("isPassed")]
    public bool IsPassed { get; set; }
}

public class MultiModelSummary
{
    [JsonPropertyName("modelACounts")]
    public int ModelACounts { get; set; }

    [JsonPropertyName("modelBCounts")]
    public int ModelBCounts { get; set; }

    [JsonPropertyName("modelCCounts")]
    public int ModelCCounts { get; set; }

    [JsonPropertyName("mostUsedModel")]
    public string MostUsedModel { get; set; } = "";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";
}

// ============================================
// Optimization Service
// ============================================

public class OptimizationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OptimizationService> _logger;
    private readonly AuthService _authService;

    public OptimizationService(IHttpClientFactory clientFactory, ILogger<OptimizationService> logger, AuthService authService)
    {
        _httpClient = clientFactory.CreateClient("Api");
        //_httpClient.Timeout = TimeSpan.FromMinutes(10);
        _logger = logger;
        _authService = authService;
    }

    /// <summary>
    /// Run Blank & Scale optimization using Differential Evolution
    /// </summary>
    public async Task<ServiceResult<BlankScaleOptimizationResult>> OptimizeAsync(BlankScaleOptimizationRequest request)
    {
        try
        {
            SetAuthHeader();

            var response = await _httpClient.PostAsJsonAsync("optimization/blank-scale", request);
            var content = await response.Content.ReadAsStringAsync();

            _logger.LogDebug("Optimization response: {Content}", content);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<BlankScaleOptimizationResult>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                    return ServiceResult<BlankScaleOptimizationResult>.Success(result.Data);

                return ServiceResult<BlankScaleOptimizationResult>.Fail(result?.Message ?? "Optimization failed");
            }

            return ServiceResult<BlankScaleOptimizationResult>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running optimization");
            return ServiceResult<BlankScaleOptimizationResult>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get current statistics without optimization
    /// </summary>
    public async Task<ServiceResult<BlankScaleOptimizationResult>> GetCurrentStatsAsync(
        Guid projectId, decimal minDiff = -10m, decimal maxDiff = 10m)
    {
        try
        {
            SetAuthHeader();

            var response = await _httpClient.GetAsync(
                $"optimization/{projectId}/statistics?minDiff={minDiff}&maxDiff={maxDiff}");
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<BlankScaleOptimizationResult>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                    return ServiceResult<BlankScaleOptimizationResult>.Success(result.Data);

                return ServiceResult<BlankScaleOptimizationResult>.Fail(result?.Message ?? "Failed");
            }

            return ServiceResult<BlankScaleOptimizationResult>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting stats");
            return ServiceResult<BlankScaleOptimizationResult>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Preview manual Blank & Scale values
    /// </summary>
    public async Task<ServiceResult<BlankScaleOptimizationResult>> PreviewManualAsync(
        Guid projectId, string element, decimal blank, decimal scale)
    {
        try
        {
            SetAuthHeader();

            var request = new { projectId, element, blank, scale };
            var response = await _httpClient.PostAsJsonAsync("optimization/preview", request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<BlankScaleOptimizationResult>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                    return ServiceResult<BlankScaleOptimizationResult>.Success(result.Data);

                return ServiceResult<BlankScaleOptimizationResult>.Fail(result?.Message ?? "Failed");
            }

            return ServiceResult<BlankScaleOptimizationResult>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error previewing optimization");
            return ServiceResult<BlankScaleOptimizationResult>.Fail($"Error: {ex.Message}");
        }
    }

    private void SetAuthHeader()
    {
        var token = _authService.GetAccessToken();
        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
    }
}
