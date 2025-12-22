using Application.DTOs;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebUI.Services;

public class UserManagementService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UserManagementService> _logger;
    private readonly AuthService _authService;

    public UserManagementService(IHttpClientFactory clientFactory, ILogger<UserManagementService> logger, AuthService authService)
    {
        _httpClient = clientFactory.CreateClient("Api");
        _logger = logger;
        _authService = authService;
    }

    /// <summary>
    /// Get all users
    /// </summary>
    public async Task<List<UserListDto>> GetAllUsersAsync()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "usermanagement/all");
            SetAuthHeader(request);
            
            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to retrieve users. Status: {StatusCode}, Response: {Content}", 
                    response.StatusCode, errorContent);
                
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    throw new UnauthorizedAccessException("You are not authorized to view the user list. Please log in again.");
                }
                
                response.EnsureSuccessStatusCode();
            }

            var content = await response.Content.ReadAsStringAsync();
            var users = JsonSerializer.Deserialize<List<UserListDto>>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            _logger.LogInformation("Retrieved users list");
            return users ?? new List<UserListDto>();
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving users");
            throw;
        }
    }

    /// <summary>
    /// Get user by ID
    /// </summary>
    public async Task<UserResponseDto?> GetUserByIdAsync(Guid userId)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"usermanagement/{userId}");
            SetAuthHeader(request);
            
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync();
            var user = JsonSerializer.Deserialize<UserResponseDto>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Create a new user
    /// </summary>
    public async Task<(bool Success, string Message, UserResponseDto? User)> CreateUserAsync(CreateUserDto createUserDto)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "usermanagement/create")
            {
                Content = JsonContent.Create(createUserDto)
            };
            SetAuthHeader(request);
            
            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var content1 = await response.Content.ReadAsStringAsync();

                try
                {
                    using var jsonDoc = JsonDocument.Parse(content1);
                    if (jsonDoc.RootElement.TryGetProperty("message", out var messageElement))
                    {
                        return (false, messageElement.GetString() ?? "An error has occurred.", null);
                    }

                    // اگر سرور خطاها را در قالب دیگری می‌فرستد (مثل مدل پیش‌فرض ASP.NET Core)
                    return (false, "این نام کاربری قبلاً انتخاب شده است یا داده‌ها معتبر نیستند", null);
                }
                catch
                {
                    return (false, "An unexpected error occurred on the server.", null);
                }
            }

            var result = JsonSerializer.Deserialize<CreateUserResponseDto>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return (result?.Success ?? false, result?.Message ?? "User created", result?.User);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user");
            throw;
        }
    }

    /// <summary>
    /// Update user password
    /// </summary>
    public async Task<(bool Success, string Message)> UpdateUserPasswordAsync(Guid userId, string newPassword)
    {
        try
        {
            var dto = new UpdateUserPasswordDto { UserId = userId, NewPassword = newPassword };
            var request = new HttpRequestMessage(HttpMethod.Post, "usermanagement/change-password")
            {
                Content = JsonContent.Create(dto)
            };
            SetAuthHeader(request);
            
            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var error = JsonSerializer.Deserialize<dynamic>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return (false, error?.message?.ToString() ?? "Error changing password");
            }

            var result = JsonSerializer.Deserialize<OperationResultDto>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return (result?.Success ?? false, result?.Message ?? "Password changed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating password for user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Delete user
    /// </summary>
    public async Task<bool> DeleteUserAsync(Guid userId)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, $"usermanagement/{userId}");
            SetAuthHeader(request);
            
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return false;

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<OperationResultDto>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return result?.Success ?? false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Set authorization header with bearer token
    /// </summary>
    private void SetAuthHeader(HttpRequestMessage request)
    {
        var token = _authService.GetAccessToken();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            _logger.LogDebug("Authorization header set for request to {Uri}", request.RequestUri);
        }
        else
        {
            _logger.LogWarning("No access token available for request to {Uri}", request.RequestUri);
        }
    }

    public async Task<(bool Success, string Message)> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        try
        {
            var model = new
            {
                CurrentPassword = currentPassword,
                NewPassword = newPassword
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "usermanagement/change-my-password")
            {
                Content = JsonContent.Create(model)
            };

            SetAuthHeader(request); // 👈 اضافه کردن هدر Authorization

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return (true, "Password changed successfully");
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            return (false, errorContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password");
            return (false, "Server connection error");
        }
    }

}

/// <summary>
/// Response DTO for create user operation
/// </summary>
public class CreateUserResponseDto
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("user")]
    public UserResponseDto? User { get; set; }
}
