using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebUI.Services;

// ============================================
// Report DTOs
// ============================================

public class ReportRequest
{
    [JsonPropertyName("projectId")]
    public Guid ProjectId { get; set; }

    [JsonPropertyName("format")]
    public string Format { get; set; } = "excel";

    [JsonPropertyName("includeStatistics")]
    public bool IncludeStatistics { get; set; } = true;

    [JsonPropertyName("includeSummary")]
    public bool IncludeSummary { get; set; } = true;

    [JsonPropertyName("includeCharts")]
    public bool IncludeCharts { get; set; } = false;

    [JsonPropertyName("useOxide")]
    public bool UseOxide { get; set; } = false;

    [JsonPropertyName("selectedElements")]
    public List<string>? SelectedElements { get; set; }

    [JsonPropertyName("elements")]
    public List<string>? Elements { get; set; }

    [JsonPropertyName("decimalPlaces")]
    public int DecimalPlaces { get; set; } = 2;
}

public class ReportResultDto
{
    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = "";

    [JsonPropertyName("generatedAt")]
    public DateTime GeneratedAt { get; set; }

    [JsonPropertyName("totalRows")]
    public int TotalRows { get; set; }

    [JsonPropertyName("totalColumns")]
    public int TotalColumns { get; set; }

    [JsonPropertyName("statistics")]
    public Dictionary<string, ColumnStatsDto>? Statistics { get; set; }

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = "";

    [JsonPropertyName("base64Data")]
    public string Base64Data { get; set; } = "";
}

// ============================================
// Report Service
// ============================================

public class ReportService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ReportService> _logger;
    private readonly AuthService _authService;

    public ReportService(IHttpClientFactory clientFactory, ILogger<ReportService> logger, AuthService authService)
    {
        _httpClient = clientFactory.CreateClient("Api");
        _logger = logger;
        _authService = authService;
    }

    /// <summary>
    /// Export to Excel
    /// </summary>
    public async Task<ServiceResult<byte[]>> ExportToExcelAsync(Guid projectId, bool useOxide = false, int decimalPlaces = 2)
    {
        try
        {
            SetAuthHeader();

            var request = new ReportRequest
            {
                ProjectId = projectId,
                Format = "excel",
                UseOxide = useOxide,
                DecimalPlaces = decimalPlaces
            };

            var response = await _httpClient.GetAsync(
                $"reports/{projectId}/excel?useOxide={useOxide}&decimalPlaces={decimalPlaces}");

            if (response.IsSuccessStatusCode)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync();
                return ServiceResult<byte[]>.Success(bytes);
            }

            return ServiceResult<byte[]>.Fail($"Export failed: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting to Excel");
            return ServiceResult<byte[]>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Export to CSV
    /// </summary>
    public async Task<ServiceResult<byte[]>> ExportToCsvAsync(Guid projectId, bool useOxide = false)
    {
        try
        {
            SetAuthHeader();

            var response = await _httpClient.GetAsync(
                $"reports/{projectId}/csv?useOxide={useOxide}");

            if (response.IsSuccessStatusCode)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync();
                return ServiceResult<byte[]>.Success(bytes);
            }

            return ServiceResult<byte[]>.Fail($"Export failed: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting to CSV");
            return ServiceResult<byte[]>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Export to JSON
    /// </summary>
    public async Task<ServiceResult<byte[]>> ExportToJsonAsync(Guid projectId)
    {
        try
        {
            SetAuthHeader();

            var response = await _httpClient.GetAsync($"reports/{projectId}/json");

            if (response.IsSuccessStatusCode)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync();
                return ServiceResult<byte[]>.Success(bytes);
            }

            return ServiceResult<byte[]>.Fail($"Export failed: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting to JSON");
            return ServiceResult<byte[]>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Generate report summary
    /// </summary>
    public async Task<ServiceResult<ReportResultDto>> GenerateReportAsync(ReportRequest request)
    {
        try
        {
            SetAuthHeader();

            var response = await _httpClient.PostAsJsonAsync("reports", request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<ReportResultDto>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                    return ServiceResult<ReportResultDto>.Success(result.Data);

                return ServiceResult<ReportResultDto>.Fail(result?.Message ?? "Failed");
            }

            return ServiceResult<ReportResultDto>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating report");
            return ServiceResult<ReportResultDto>.Fail($"Error: {ex.Message}");
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
