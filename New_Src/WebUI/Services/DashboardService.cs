using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebUI.Services;

public class ApiEnvelope<T>
{
    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("messages")]
    public List<string>? Messages { get; set; }

    public string? Message => Messages?.FirstOrDefault();
}

public class HealthStatusDto
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("service")]
    public string Service { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
}

public class ImportJobDto
{
    [JsonPropertyName("jobId")]
    public Guid JobId { get; set; }

    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = "";

    [JsonPropertyName("state")]
    public int State { get; set; }

    [JsonPropertyName("totalRows")]
    public int TotalRows { get; set; }

    [JsonPropertyName("processedRows")]
    public int ProcessedRows { get; set; }

    [JsonPropertyName("percent")]
    public double Percent { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("resultProjectId")]
    public Guid? ResultProjectId { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}

public class ImportJobPage
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("items")]
    public List<ImportJobDto> Items { get; set; } = new();
}

public class DashboardService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DashboardService> _logger;
    private readonly AuthService _authService;

    public DashboardService(IHttpClientFactory factory, ILogger<DashboardService> logger, AuthService authService)
    {
        _httpClient = factory.CreateClient("Api");
        _logger = logger;
        _authService = authService;
    }

    public async Task<ServiceResult<HealthStatusDto>> GetHealthAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("health");
            var content = await response.Content.ReadAsStringAsync();

            var health = JsonSerializer.Deserialize<HealthStatusDto>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (health == null)
            {
                return ServiceResult<HealthStatusDto>.Fail("Cannot parse health response");
            }

            return ServiceResult<HealthStatusDto>.Success(health);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load health status");
            return ServiceResult<HealthStatusDto>.Fail("API health endpoint is not reachable");
        }
    }

    public async Task<ServiceResult<ImportJobPage>> GetImportJobsAsync(int page = 1, int pageSize = 5)
    {
        try
        {
            SetAuthHeader();
            var response = await _httpClient.GetAsync($"projects/import/jobs?page={page}&pageSize={pageSize}");
            var content = await response.Content.ReadAsStringAsync();

            var envelope = JsonSerializer.Deserialize<ApiEnvelope<ImportJobPage>>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (envelope?.Succeeded == true && envelope.Data != null)
            {
                return ServiceResult<ImportJobPage>.Success(envelope.Data);
            }

            return ServiceResult<ImportJobPage>.Fail(envelope?.Message ?? "Unable to load jobs");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load import jobs");
            return ServiceResult<ImportJobPage>.Fail("Cannot connect to API");
        }
    }

    private void SetAuthHeader()
    {
        var token = _authService.GetCurrentUser()?.Token;
        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }
}
