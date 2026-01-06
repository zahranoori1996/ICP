using System.Text;
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

    [JsonPropertyName("previewOnly")]
    public bool PreviewOnly { get; set; } = false;

    [JsonPropertyName("seed")]
    public int? Seed { get; set; }

    // Acceptable Ranges (Python: calculate_dynamic_range)
    [JsonPropertyName("rangeLow")]
    public decimal RangeLow { get; set; } = 2.0m;

    [JsonPropertyName("rangeMid")]
    public decimal RangeMid { get; set; } = 20.0m;

    [JsonPropertyName("rangeHigh1")]
    public decimal RangeHigh1 { get; set; } = 10.0m;

    [JsonPropertyName("rangeHigh2")]
    public decimal RangeHigh2 { get; set; } = 8.0m;

    [JsonPropertyName("rangeHigh3")]
    public decimal RangeHigh3 { get; set; } = 5.0m;

    [JsonPropertyName("rangeHigh4")]
    public decimal RangeHigh4 { get; set; } = 3.0m;

    // Scale Application Range
    [JsonPropertyName("scaleRangeMin")]
    public decimal? ScaleRangeMin { get; set; }

    [JsonPropertyName("scaleRangeMax")]
    public decimal? ScaleRangeMax { get; set; }

    [JsonPropertyName("scaleAbove50Only")]
    public bool ScaleAbove50Only { get; set; } = false;

    [JsonPropertyName("crmSelections")]
    public Dictionary<string, string>? CrmSelections { get; set; }

    [JsonPropertyName("includedCrmIds")]
    public List<string>? IncludedCrmIds { get; set; }

    [JsonPropertyName("excludedSolutionLabels")]
    public List<string>? ExcludedSolutionLabels { get; set; }
}

public class CrmMethodOptionDto
{
    [JsonPropertyName("crmId")]
    public string CrmId { get; set; } = "";

    [JsonPropertyName("methods")]
    public List<string> Methods { get; set; } = new();

    [JsonPropertyName("defaultMethod")]
    public string? DefaultMethod { get; set; }
}

public class CrmOptionsResult
{
    [JsonPropertyName("items")]
    public List<CrmMethodOptionDto> Items { get; set; } = new();
}

public class CrmSelectionRowDto
{
    [JsonPropertyName("solutionLabel")]
    public string SolutionLabel { get; set; } = "";

    [JsonPropertyName("rowIndex")]
    public int RowIndex { get; set; }

    [JsonPropertyName("crmId")]
    public string CrmId { get; set; } = "";

    [JsonPropertyName("preferredOptions")]
    public List<string> PreferredOptions { get; set; } = new();

    [JsonPropertyName("allOptions")]
    public List<string> AllOptions { get; set; } = new();

    [JsonPropertyName("selectedOption")]
    public string? SelectedOption { get; set; }
}

public class CrmSelectionOptionsResult
{
    [JsonPropertyName("items")]
    public List<CrmSelectionRowDto> Items { get; set; } = new();
}

public class CrmSelectionItemDto
{
    [JsonPropertyName("solutionLabel")]
    public string SolutionLabel { get; set; } = "";

    [JsonPropertyName("rowIndex")]
    public int RowIndex { get; set; }

    [JsonPropertyName("selectedCrmKey")]
    public string SelectedCrmKey { get; set; } = "";
}

public class CrmSelectionSaveRequest
{
    [JsonPropertyName("projectId")]
    public Guid ProjectId { get; set; }

    [JsonPropertyName("selections")]
    public List<CrmSelectionItemDto> Selections { get; set; } = new();
}

public class BlankScaleOptimizationResult
{
    [JsonPropertyName("totalSamples")]
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

    [JsonPropertyName("optimalBlank")]
    public decimal Blank { get; set; }

    [JsonPropertyName("optimalScale")]
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

    [JsonPropertyName("originalValues")]
    public Dictionary<string, decimal?> OriginalValues { get; set; } = new();

    [JsonPropertyName("crmValues")]
    public Dictionary<string, decimal?> CrmValues { get; set; } = new();

    [JsonPropertyName("optimizedValues")]
    public Dictionary<string, decimal?> OptimizedValues { get; set; } = new();

    [JsonPropertyName("diffPercentBefore")]
    public Dictionary<string, decimal> DiffPercentBefore { get; set; } = new();

    [JsonPropertyName("diffPercentAfter")]
    public Dictionary<string, decimal> DiffPercentAfter { get; set; } = new();

    [JsonPropertyName("passStatusBefore")]
    public Dictionary<string, bool> PassStatusBefore { get; set; } = new();

    [JsonPropertyName("passStatusAfter")]
    public Dictionary<string, bool> PassStatusAfter { get; set; } = new();
}

public class MultiModelSummary
{
    [JsonPropertyName("elementsOptimizedWithModelA")]
    public int ModelACounts { get; set; }

    [JsonPropertyName("elementsOptimizedWithModelB")]
    public int ModelBCounts { get; set; }

    [JsonPropertyName("elementsOptimizedWithModelC")]
    public int ModelCCounts { get; set; }

    [JsonPropertyName("mostUsedModel")]
    public string MostUsedModel { get; set; } = "";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";
}

public class ManualBlankScaleResult
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

    [JsonPropertyName("optimizedData")]
    public List<OptimizedSampleDto> OptimizedData { get; set; } = new();
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
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
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

    public async Task<ServiceResult<CrmOptionsResult>> GetCrmOptionsAsync(Guid projectId)
    {
        try
        {
            SetAuthHeader();

            var response = await _httpClient.GetAsync($"optimization/{projectId}/crm-options");
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<CrmOptionsResult>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                    return ServiceResult<CrmOptionsResult>.Success(result.Data);

                return ServiceResult<CrmOptionsResult>.Fail(result?.Message ?? "Failed to load CRM options");
            }

            return ServiceResult<CrmOptionsResult>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading CRM options");
            return ServiceResult<CrmOptionsResult>.Fail($"Error: {ex.Message}");
        }
    }

    public async Task<ServiceResult<CrmSelectionOptionsResult>> GetCrmSelectionOptionsAsync(Guid projectId)
    {
        try
        {
            SetAuthHeader();

            var response = await _httpClient.GetAsync($"optimization/{projectId}/crm-selection-options");
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<CrmSelectionOptionsResult>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                    return ServiceResult<CrmSelectionOptionsResult>.Success(result.Data);

                return ServiceResult<CrmSelectionOptionsResult>.Fail(result?.Message ?? "Failed to load CRM selections");
            }

            return ServiceResult<CrmSelectionOptionsResult>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading CRM selections");
            return ServiceResult<CrmSelectionOptionsResult>.Fail($"Error: {ex.Message}");
        }
    }

    public async Task<ServiceResult<bool>> SaveCrmSelectionsAsync(CrmSelectionSaveRequest request)
    {
        try
        {
            SetAuthHeader();

            var json = JsonSerializer.Serialize(request);
            var response = await _httpClient.PostAsync(
                $"optimization/{request.ProjectId}/crm-selections",
                new StringContent(json, Encoding.UTF8, "application/json"));

            var content = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<bool>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true)
                    return ServiceResult<bool>.Success(true);

                return ServiceResult<bool>.Fail(result?.Message ?? "Failed to save CRM selections");
            }

            return ServiceResult<bool>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving CRM selections");
            return ServiceResult<bool>.Fail($"Error: {ex.Message}");
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

    /// <summary>
    /// Preview manual Blank & Scale values (ManualBlankScaleResult)
    /// </summary>
    public async Task<ServiceResult<ManualBlankScaleResult>> PreviewManualDetailsAsync(
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
                var result = JsonSerializer.Deserialize<ApiResult<ManualBlankScaleResult>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                    return ServiceResult<ManualBlankScaleResult>.Success(result.Data);

                return ServiceResult<ManualBlankScaleResult>.Fail(result?.Message ?? "Failed");
            }

            return ServiceResult<ManualBlankScaleResult>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error previewing manual optimization");
            return ServiceResult<ManualBlankScaleResult>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply manual Blank & Scale values
    /// </summary>
    public async Task<ServiceResult<ManualBlankScaleResult>> ApplyManualAsync(
        Guid projectId, string element, decimal blank, decimal scale)
    {
        try
        {
            SetAuthHeader();

            var request = new { projectId, element, blank, scale };
            var response = await _httpClient.PostAsJsonAsync("optimization/apply", request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<ManualBlankScaleResult>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                    return ServiceResult<ManualBlankScaleResult>.Success(result.Data);

                return ServiceResult<ManualBlankScaleResult>.Fail(result?.Message ?? "Failed");
            }

            return ServiceResult<ManualBlankScaleResult>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying manual optimization");
            return ServiceResult<ManualBlankScaleResult>.Fail($"Error: {ex.Message}");
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
