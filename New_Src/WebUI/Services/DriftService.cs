using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebUI.Services;

// ============================================
// Drift DTOs
// ============================================

public enum DriftMethod
{
    None = 0,
    Linear = 1,
    Stepwise = 2,
    Polynomial = 3
}

public class DriftCorrectionRequest
{
    [JsonPropertyName("projectId")]
    public Guid ProjectId { get; set; }

    [JsonPropertyName("selectedElements")]
    public List<string>? SelectedElements { get; set; }

    [JsonPropertyName("method")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DriftMethod Method { get; set; } = DriftMethod.Linear;

    [JsonPropertyName("useSegmentation")]
    public bool UseSegmentation { get; set; } = true;

    [JsonPropertyName("basePattern")]
    public string? BasePattern { get; set; }

    [JsonPropertyName("conePattern")]
    public string? ConePattern { get; set; }
}

public class DriftCorrectionResult
{
    [JsonPropertyName("totalSamples")]
    public int TotalSamples { get; set; }

    [JsonPropertyName("correctedSamples")]
    public int CorrectedSamples { get; set; }

    [JsonPropertyName("segmentsFound")]
    public int SegmentsFound { get; set; }

    [JsonPropertyName("segments")]
    public List<DriftSegment> Segments { get; set; } = new();

    [JsonPropertyName("elementDrifts")]
    public Dictionary<string, ElementDriftInfo> ElementDrifts { get; set; } = new();

    [JsonPropertyName("correctedData")]
    public List<CorrectedSampleDto> CorrectedData { get; set; } = new();
}

public class DriftSegment
{
    [JsonPropertyName("segmentIndex")]
    public int SegmentIndex { get; set; }

    [JsonPropertyName("startIndex")]
    public int StartIndex { get; set; }

    [JsonPropertyName("endIndex")]
    public int EndIndex { get; set; }

    [JsonPropertyName("startStandard")]
    public string? StartStandard { get; set; }

    [JsonPropertyName("endStandard")]
    public string? EndStandard { get; set; }

    [JsonPropertyName("sampleCount")]
    public int SampleCount { get; set; }
}

public class ElementDriftInfo
{
    [JsonPropertyName("element")]
    public string Element { get; set; } = "";

    [JsonPropertyName("initialRatio")]
    public decimal InitialRatio { get; set; }

    [JsonPropertyName("finalRatio")]
    public decimal FinalRatio { get; set; }

    [JsonPropertyName("driftPercent")]
    public decimal DriftPercent { get; set; }

    [JsonPropertyName("slope")]
    public decimal Slope { get; set; }

    [JsonPropertyName("intercept")]
    public decimal Intercept { get; set; }
}

public class CorrectedSampleDto
{
    [JsonPropertyName("solutionLabel")]
    public string SolutionLabel { get; set; } = "";

    [JsonPropertyName("originalIndex")]
    public int OriginalIndex { get; set; }

    [JsonPropertyName("segmentIndex")]
    public int SegmentIndex { get; set; }

    [JsonPropertyName("originalValues")]
    public Dictionary<string, decimal?> OriginalValues { get; set; } = new();

    [JsonPropertyName("correctedValues")]
    public Dictionary<string, decimal?> CorrectedValues { get; set; } = new();

    [JsonPropertyName("correctionFactors")]
    public Dictionary<string, decimal> CorrectionFactors { get; set; } = new();
}

// ============================================
// Drift Service
// ============================================

public class DriftService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DriftService> _logger;
    private readonly AuthService _authService;

    public DriftService(IHttpClientFactory clientFactory, ILogger<DriftService> logger, AuthService authService)
    {
        _httpClient = clientFactory.CreateClient("Api");
        _logger = logger;
        _authService = authService;
    }

    /// <summary>
    /// Analyze drift without applying correction
    /// </summary>
    public async Task<ServiceResult<DriftCorrectionResult>> AnalyzeDriftAsync(DriftCorrectionRequest request)
    {
        try
        {
            SetAuthHeader();

            var response = await _httpClient.PostAsJsonAsync("drift/analyze", request);
            var content = await response.Content.ReadAsStringAsync();

            _logger.LogDebug("Drift analysis response: {Content}", content);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<DriftCorrectionResult>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                    return ServiceResult<DriftCorrectionResult>.Success(result.Data);

                return ServiceResult<DriftCorrectionResult>.Fail(result?.Message ?? "Failed");
            }

            return ServiceResult<DriftCorrectionResult>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing drift");
            return ServiceResult<DriftCorrectionResult>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply drift correction
    /// </summary>
    public async Task<ServiceResult<DriftCorrectionResult>> ApplyDriftCorrectionAsync(DriftCorrectionRequest request)
    {
        try
        {
            SetAuthHeader();

            var response = await _httpClient.PostAsJsonAsync("drift/correct", request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<DriftCorrectionResult>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                    return ServiceResult<DriftCorrectionResult>.Success(result.Data);

                return ServiceResult<DriftCorrectionResult>.Fail(result?.Message ?? "Failed");
            }

            return ServiceResult<DriftCorrectionResult>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying drift correction");
            return ServiceResult<DriftCorrectionResult>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Detect segments in data
    /// </summary>
    public async Task<ServiceResult<List<DriftSegment>>> DetectSegmentsAsync(Guid projectId)
    {
        try
        {
            SetAuthHeader();

            var response = await _httpClient.GetAsync($"drift/{projectId}/segments");
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<List<DriftSegment>>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                    return ServiceResult<List<DriftSegment>>.Success(result.Data);

                return ServiceResult<List<DriftSegment>>.Fail(result?.Message ?? "Failed");
            }

            return ServiceResult<List<DriftSegment>>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting segments");
            return ServiceResult<List<DriftSegment>>.Fail($"Error: {ex.Message}");
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
