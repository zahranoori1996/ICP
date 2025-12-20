using Application.DTOs;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebUI.Services;

public class UserManagementService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UserManagementService> _logger;

    public UserManagementService(IHttpClientFactory clientFactory, ILogger<UserManagementService> logger)
    {
        _httpClient = clientFactory.CreateClient("Api");
        _logger = logger;
    }

    /// <summary>
    /// Get all users
    /// </summary>
    public async Task<List<UserListDto>> GetAllUsersAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("usermanagement/all");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var users = JsonSerializer.Deserialize<List<UserListDto>>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            _logger.LogInformation("Retrieved users list");
            return users ?? new List<UserListDto>();
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
            var response = await _httpClient.GetAsync($"usermanagement/{userId}");

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
            var response = await _httpClient.PostAsJsonAsync("usermanagement/create", createUserDto);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var error = JsonSerializer.Deserialize<dynamic>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return (false, error?.message?.ToString() ?? "خطا در ایجاد کاربر", null);
            }

            var result = JsonSerializer.Deserialize<CreateUserResponseDto>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return (result?.Success ?? false, result?.Message ?? "کاربر ایجاد شد", result?.User);
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
            var response = await _httpClient.PostAsJsonAsync("usermanagement/change-password", dto);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var error = JsonSerializer.Deserialize<dynamic>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return (false, error?.message?.ToString() ?? "خطا در تغییر رمز عبور");
            }

            var result = JsonSerializer.Deserialize<OperationResultDto>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return (result?.Success ?? false, result?.Message ?? "رمز عبور تغییر کرد");
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
            var response = await _httpClient.DeleteAsync($"usermanagement/{userId}");

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
