using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebUI.Services;

// ============================================
// CRM DTOs
// ============================================

public class CrmListItemDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("crmId")]
    public string CrmId { get; set; } = "";

    [JsonPropertyName("analysisMethod")]
    public string? AnalysisMethod { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("elements")]
    public Dictionary<string, decimal> Elements { get; set; } = new();

    [JsonPropertyName("isOurOreas")]
    public bool IsOurOreas { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
    
    // Compatibility alias
    public Dictionary<string, decimal> ElementValues => Elements;
}

public class CrmDiffResultDto
{
    [JsonPropertyName("solutionLabel")]
    public string SolutionLabel { get; set; } = "";

    [JsonPropertyName("crmId")]
    public string CrmId { get; set; } = "";

    [JsonPropertyName("differences")]
    public List<ElementDiffDto> Differences { get; set; } = new();

    [JsonPropertyName("passCount")]
    public int PassCount { get; set; }

    [JsonPropertyName("failCount")]
    public int FailCount { get; set; }
}

public class ElementDiffDto
{
    [JsonPropertyName("element")]
    public string Element { get; set; } = "";

    [JsonPropertyName("measuredValue")]
    public decimal MeasuredValue { get; set; }

    [JsonPropertyName("crmValue")]
    public decimal CrmValue { get; set; }

    [JsonPropertyName("differencePercent")]
    public decimal DifferencePercent { get; set; }

    [JsonPropertyName("isPassed")]
    public bool IsPassed { get; set; }
}

public class PaginatedResult<T>
{
    [JsonPropertyName("items")]
    public List<T> Items { get; set; } = new();

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

// ============================================
// CRM Service
// ============================================

public class CrmService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CrmService> _logger;
    private readonly AuthService _authService;

    public CrmService(IHttpClientFactory clientFactory, ILogger<CrmService> logger, AuthService authService)
    {
        _httpClient = clientFactory.CreateClient("Api");
        _logger = logger;
        _authService = authService;
    }

    /// <summary>
    /// Get CRM list with filtering
    /// </summary>
    public async Task<ServiceResult<PaginatedResult<CrmListItemDto>>> GetCrmListAsync(
        string? analysisMethod = null,
        string? search = null,
        bool? ourOreasOnly = null,
        int page = 1,
        int pageSize = 50)
    {
        try
        {
            SetAuthHeader();

            var url = $"crm?page={page}&pageSize={pageSize}";
            if (!string.IsNullOrEmpty(analysisMethod))
                url += $"&analysisMethod={Uri.EscapeDataString(analysisMethod)}";
            if (!string.IsNullOrEmpty(search))
                url += $"&searchText={Uri.EscapeDataString(search)}";
            if (ourOreasOnly == true)
                url += "&ourOreasOnly=true";

            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<PaginatedResult<CrmListItemDto>>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                    return ServiceResult<PaginatedResult<CrmListItemDto>>.Success(result.Data);

                return ServiceResult<PaginatedResult<CrmListItemDto>>.Fail(result?.Message ?? "Failed");
            }

            return ServiceResult<PaginatedResult<CrmListItemDto>>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading CRM list");
            return ServiceResult<PaginatedResult<CrmListItemDto>>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get analysis methods
    /// </summary>
    public async Task<ServiceResult<List<string>>> GetAnalysisMethodsAsync()
    {
        try
        {
            SetAuthHeader();

            var response = await _httpClient.GetAsync("crm/methods");
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<List<string>>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                    return ServiceResult<List<string>>.Success(result.Data);

                return ServiceResult<List<string>>.Fail(result?.Message ?? "Failed");
            }

            return ServiceResult<List<string>>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading analysis methods");
            return ServiceResult<List<string>>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Calculate CRM differences for a project
    /// </summary>
    public async Task<ServiceResult<List<CrmDiffResultDto>>> CalculateDiffAsync(
        Guid projectId,
        List<string>? crmPatterns = null,
        decimal minDiff = -10m,
        decimal maxDiff = 10m)
    {
        try
        {
            SetAuthHeader();

            var request = new { projectId, crmPatterns, minDiff, maxDiff };
            var response = await _httpClient.PostAsJsonAsync("crm/diff", request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<List<CrmDiffResultDto>>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                    return ServiceResult<List<CrmDiffResultDto>>.Success(result.Data);

                return ServiceResult<List<CrmDiffResultDto>>.Fail(result?.Message ?? "Failed");
            }

            return ServiceResult<List<CrmDiffResultDto>>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating CRM diff");
            return ServiceResult<List<CrmDiffResultDto>>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Create or update CRM
    /// </summary>
    public async Task<ServiceResult<CrmListItemDto>> SaveCrmAsync(CrmListItemDto crm)
    {
        try
        {
            SetAuthHeader();

            HttpResponseMessage response;
            if (crm.Id > 0)
            {
                response = await _httpClient.PutAsJsonAsync($"crm/{crm.Id}", crm);
            }
            else
            {
                response = await _httpClient.PostAsJsonAsync("crm", crm);
            }

            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<CrmListItemDto>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                    return ServiceResult<CrmListItemDto>.Success(result.Data);

                return ServiceResult<CrmListItemDto>.Fail(result?.Message ?? "Failed");
            }

            return ServiceResult<CrmListItemDto>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving CRM");
            return ServiceResult<CrmListItemDto>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Delete CRM
    /// </summary>
    public async Task<ServiceResult<bool>> DeleteCrmAsync(int id)
    {
        try
        {
            SetAuthHeader();

            var response = await _httpClient.DeleteAsync($"crm/{id}");

            if (response.IsSuccessStatusCode)
                return ServiceResult<bool>.Success(true);

            return ServiceResult<bool>.Fail($"Failed to delete CRM");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting CRM");
            return ServiceResult<bool>.Fail($"Error: {ex.Message}");
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
