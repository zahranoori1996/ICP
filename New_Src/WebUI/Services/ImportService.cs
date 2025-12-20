using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Forms;

namespace WebUI.Services;

// ============================================
// DTOs
// ============================================

public class PreviewResult
{
    [JsonPropertyName("detectedFormat")]
    public string DetectedFormat { get; set; } = "";

    [JsonPropertyName("totalRows")]
    public int TotalRows { get; set; }

    [JsonPropertyName("headers")]
    public List<string> Columns { get; set; } = new();

    [JsonPropertyName("previewRows")]
    public List<Dictionary<string, object?>> SampleRows { get; set; } = new();
    
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public class ImportResult
{
    [JsonPropertyName("projectId")]
    public Guid ProjectId { get; set; }

    [JsonPropertyName("projectName")]
    public string? ProjectName { get; set; }

    [JsonPropertyName("rowsImported")]
    public int RowsImported { get; set; }
}

public class ApiResult<T>
{
    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("messages")]
    public List<string>? Messages { get; set; }

    public string? Message => Messages?.FirstOrDefault();
}

public class ServiceResult<T>
{
    public bool Succeeded { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }

    public static ServiceResult<T> Success(T data) => new() { Succeeded = true, Data = data };
    public static ServiceResult<T> Fail(string message) => new() { Succeeded = false, Message = message };
}

// ============================================
// Import Service
// ============================================

public class ImportService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ImportService> _logger;
    private readonly AuthService _authService;

    public ImportService(IHttpClientFactory clientFactory, ILogger<ImportService> logger, AuthService authService)
    {
        _httpClient = clientFactory.CreateClient("Api");
        _logger = logger;
        _authService = authService;
    }

    /// <summary>
    /// Preview file before import (using byte array)
    /// </summary>
    public async Task<ServiceResult<PreviewResult>> PreviewFileAsync(byte[] fileContent, string fileName, string contentType, int previewRows = 10)
    {
        try
        {
            SetAuthHeader();

            using var content = new MultipartFormDataContent();

            // Add file from byte array
            var fileStreamContent = new ByteArrayContent(fileContent);
            fileStreamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Add(fileStreamContent, "file", fileName);

            // Add preview rows parameter
            content.Add(new StringContent(previewRows.ToString()), "previewRows");

            _logger.LogInformation("Previewing file: {FileName}, Size: {Size} bytes", fileName, fileContent.Length);

            var response = await _httpClient.PostAsync("import/preview", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            _logger.LogDebug("Preview response: {Content}", responseContent);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<PreviewResult>>(responseContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                {
                    return ServiceResult<PreviewResult>.Success(result.Data);
                }

                return ServiceResult<PreviewResult>.Fail(result?.Message ?? "Preview failed");
            }

            // Try to parse error
            try
            {
                var errorResult = JsonSerializer.Deserialize<ApiResult<object>>(responseContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return ServiceResult<PreviewResult>.Fail(errorResult?.Message ?? $"Server error: {response.StatusCode}");
            }
            catch
            {
                return ServiceResult<PreviewResult>.Fail($"Server error: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preview error");
            return ServiceResult<PreviewResult>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Preview file before import (using IBrowserFile - legacy)
    /// </summary>
    public async Task<ServiceResult<PreviewResult>> PreviewFileAsync(IBrowserFile file, int previewRows = 10)
    {
        try
        {
            SetAuthHeader();

            using var content = new MultipartFormDataContent();

            // Add file
            var fileContent = new StreamContent(file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024));
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType ?? "application/octet-stream");
            content.Add(fileContent, "file", file.Name);

            // Add preview rows parameter
            content.Add(new StringContent(previewRows.ToString()), "previewRows");

            var response = await _httpClient.PostAsync("import/preview", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            _logger.LogDebug("Preview response: {Content}", responseContent);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<PreviewResult>>(responseContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                {
                    return ServiceResult<PreviewResult>.Success(result.Data);
                }

                return ServiceResult<PreviewResult>.Fail(result?.Message ?? "Preview failed");
            }

            return ServiceResult<PreviewResult>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preview error");
            return ServiceResult<PreviewResult>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Import file with advanced options (using byte array)
    /// </summary>
    public async Task<ServiceResult<ImportResult>> ImportAdvancedAsync(
        byte[] fileContent,
        string fileName,
        string contentType,
        string projectName,
        string? owner = null,
        string? delimiter = null,
        int? headerRow = null,
        bool skipLastRow = true,
        bool autoDetectType = true)
    {
        try
        {
            SetAuthHeader();

            using var content = new MultipartFormDataContent();

            // Add file from byte array
            var fileStreamContent = new ByteArrayContent(fileContent);
            fileStreamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Add(fileStreamContent, "file", fileName);

            // Add parameters
            content.Add(new StringContent(projectName), "projectName");

            if (!string.IsNullOrEmpty(owner))
                content.Add(new StringContent(owner), "owner");

            if (!string.IsNullOrEmpty(delimiter))
                content.Add(new StringContent(delimiter), "delimiter");

            if (headerRow.HasValue)
                content.Add(new StringContent(headerRow.Value.ToString()), "headerRow");

            content.Add(new StringContent(skipLastRow.ToString().ToLower()), "skipLastRow");
            content.Add(new StringContent(autoDetectType.ToString().ToLower()), "autoDetectType");

            _logger.LogInformation("Importing file: {FileName} as project: {ProjectName}, Size: {Size} bytes", fileName, projectName, fileContent.Length);

            var response = await _httpClient.PostAsync("import/advanced", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            _logger.LogDebug("Import response: {Content}", responseContent);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<ImportResult>>(responseContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                {
                    _logger.LogInformation("Import successful. ProjectId: {ProjectId}", result.Data.ProjectId);
                    return ServiceResult<ImportResult>.Success(result.Data);
                }

                return ServiceResult<ImportResult>.Fail(result?.Message ?? "Import failed");
            }

            // Try to parse error message
            try
            {
                var errorResult = JsonSerializer.Deserialize<ApiResult<object>>(responseContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return ServiceResult<ImportResult>.Fail(errorResult?.Message ?? $"Server error: {response.StatusCode}");
            }
            catch
            {
                return ServiceResult<ImportResult>.Fail($"Server error: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import error");
            return ServiceResult<ImportResult>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Import file with advanced options (using IBrowserFile - legacy)
    /// </summary>
    public async Task<ServiceResult<ImportResult>> ImportAdvancedAsync(
        IBrowserFile file,
        string projectName,
        string? owner = null,
        string? delimiter = null,
        int? headerRow = null,
        bool skipLastRow = true,
        bool autoDetectType = true)
    {
        try
        {
            SetAuthHeader();

            using var content = new MultipartFormDataContent();

            // Add file (max 200MB)
            var fileContent = new StreamContent(file.OpenReadStream(maxAllowedSize: 200 * 1024 * 1024));
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType ?? "application/octet-stream");
            content.Add(fileContent, "file", file.Name);

            // Add parameters
            content.Add(new StringContent(projectName), "projectName");

            if (!string.IsNullOrEmpty(owner))
                content.Add(new StringContent(owner), "owner");

            if (!string.IsNullOrEmpty(delimiter))
                content.Add(new StringContent(delimiter), "delimiter");

            if (headerRow.HasValue)
                content.Add(new StringContent(headerRow.Value.ToString()), "headerRow");

            content.Add(new StringContent(skipLastRow.ToString().ToLower()), "skipLastRow");
            content.Add(new StringContent(autoDetectType.ToString().ToLower()), "autoDetectType");

            _logger.LogInformation("Importing file: {FileName} as project: {ProjectName}", file.Name, projectName);

            var response = await _httpClient.PostAsync("import/advanced", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            _logger.LogDebug("Import response: {Content}", responseContent);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<ImportResult>>(responseContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                {
                    _logger.LogInformation("Import successful.  ProjectId: {ProjectId}", result.Data.ProjectId);
                    return ServiceResult<ImportResult>.Success(result.Data);
                }

                return ServiceResult<ImportResult>.Fail(result?.Message ?? "Import failed");
            }

            // Try to parse error message
            try
            {
                var errorResult = JsonSerializer.Deserialize<ApiResult<object>>(responseContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return ServiceResult<ImportResult>.Fail(errorResult?.Message ?? $"Server error: {response.StatusCode}");
            }
            catch
            {
                return ServiceResult<ImportResult>.Fail($"Server error: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import error");
            return ServiceResult<ImportResult>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Simple CSV import
    /// </summary>
    public async Task<ServiceResult<ImportResult>> ImportCsvAsync(IBrowserFile file, string projectName, string? owner = null)
    {
        try
        {
            SetAuthHeader();

            using var content = new MultipartFormDataContent();

            var fileContent = new StreamContent(file.OpenReadStream(maxAllowedSize: 200 * 1024 * 1024));
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType ?? "application/octet-stream");
            content.Add(fileContent, "file", file.Name);
            content.Add(new StringContent(projectName), "projectName");

            if (!string.IsNullOrEmpty(owner))
                content.Add(new StringContent(owner), "owner");

            var response = await _httpClient.PostAsync("import/import", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<ImportResult>>(responseContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                {
                    return ServiceResult<ImportResult>.Success(result.Data);
                }

                return ServiceResult<ImportResult>.Fail(result?.Message ?? "Import failed");
            }

            return ServiceResult<ImportResult>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import error");
            return ServiceResult<ImportResult>.Fail($"Error: {ex.Message}");
        }
    }

    private void SetAuthHeader()
    {
        var token = _authService.GetAccessToken();
        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }
    }
}