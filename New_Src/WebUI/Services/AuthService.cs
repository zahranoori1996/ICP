using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebUI.Services;

// ============================================
// DTOs - consistent with new API format
// ============================================

public class LoginRequest
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    [JsonPropertyName("rememberMe")]
    public bool RememberMe { get; set; }
}

public class RegisterRequest
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    [JsonPropertyName("fullName")]
    public string? FullName { get; set; }

    [JsonPropertyName("position")]
    public string? Position { get; set; }
}

public class ApiLoginResponse
{
    // Real API format
    [JsonPropertyName("isAuthenticated")]
    public bool IsAuthenticated { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("token")]
    public string Token { get; set; } = "";

    [JsonPropertyName("position")]
    public string Position { get; set; } = "";
}

public class ApiUserInfo
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("email")]
    public string Email { get; set; } = "";

    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = "";

    [JsonPropertyName("lastName")]
    public string LastName { get; set; } = "";

    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }
}

public class ApiRegisterResponse
{
    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

public class CurrentUserResponse
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("position")]
    public string Position { get; set; } = "";
}

public record AuthResult(
    bool IsAuthenticated,
    string Message,
    string Name = "User",
    string Token = "",
    string Position = "User"
);

// ============================================
// Auth Service
// ============================================

public class AuthService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AuthService> _logger;
    private static AuthResult? _currentUser;

    public AuthService(IHttpClientFactory clientFactory, ILogger<AuthService> logger)
    {
        _httpClient = clientFactory.CreateClient("Api");
        _logger = logger;
    }

    /// <summary>
    /// User login
    /// </summary>
    public async Task<AuthResult> LoginAsync(string username, string password, bool rememberMe = false)
    {
        try
        {
            var request = new LoginRequest
            {
                Username = username,
                Password = password,
                RememberMe = rememberMe
            };

            _logger.LogInformation("Attempting login for user: {Username}", username);

            var response = await _httpClient.PostAsJsonAsync("auth/login", request);
            var content = await response.Content.ReadAsStringAsync();

            _logger.LogDebug("Login response: {Content}", content);

            var loginResponse = JsonSerializer.Deserialize<ApiLoginResponse>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (loginResponse == null)
            {
                return new AuthResult(false, "Invalid server response");
            }

            if (loginResponse.IsAuthenticated && !string.IsNullOrEmpty(loginResponse.Token))
            {
                _currentUser = new AuthResult(
                    IsAuthenticated: true,
                    Message: "Login successful",
                    Name: string.IsNullOrWhiteSpace(loginResponse.Name) ? username : loginResponse.Name,
                    Token: loginResponse.Token,
                    Position: string.IsNullOrWhiteSpace(loginResponse.Position) ? "User" : loginResponse.Position
                );

                // set bearer token for subsequent calls
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", loginResponse.Token);

                _logger.LogInformation("User {Username} logged in successfully", username);
                return _currentUser;
            }

            var errorMessage = loginResponse.Message ?? "Invalid username or password";
            _logger.LogWarning("Login failed for {Username}: {Error}", username, errorMessage);
            return new AuthResult(false, errorMessage);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Cannot connect to API");
            return new AuthResult(false, "Cannot connect to server. Please try again later.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login error");
            return new AuthResult(false, "An error occurred during login.");
        }
    }

    /// <summary>
    /// User registration
    /// </summary>
    public async Task<AuthResult> RegisterAsync(string username, string password, string? fullName, string? position)
    {
        try
        {
            var request = new RegisterRequest
            {
                Username = username,
                Password = password,
                FullName = string.IsNullOrWhiteSpace(fullName) ? username : fullName,
                Position = string.IsNullOrWhiteSpace(position) ? "Analyst" : position
            };

            _logger.LogInformation("Attempting registration for user: {Username}", username);

            var response = await _httpClient.PostAsJsonAsync("auth/register", request);
            var content = await response.Content.ReadAsStringAsync();

            _logger.LogDebug("Register response: {Content}", content);

            var registerResponse = JsonSerializer.Deserialize<ApiRegisterResponse>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (registerResponse?.Succeeded == true)
            {
                _logger.LogInformation("User {Username} registered successfully", username);
                return new AuthResult(true, registerResponse.Message);
            }

            var error = registerResponse?.Message ?? "Registration failed";
            return new AuthResult(false, error);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Cannot connect to API for registration");
            return new AuthResult(false, "Cannot connect to server. Please try again later.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration error");
            return new AuthResult(false, "An error occurred during registration.");
        }
    }

    /// <summary>
    /// Check for active session from token in memory
    /// </summary>
    public Task<AuthResult> CheckAutoLoginAsync()
    {
        if (_currentUser != null && _currentUser.IsAuthenticated)
        {
            return Task.FromResult(_currentUser);
        }

        return Task.FromResult(new AuthResult(false, "No active session"));
    }

    /// <summary>
    /// Refresh user info from API using current token
    /// </summary>
    public async Task<AuthResult?> RefreshCurrentUserAsync()
    {
        if (_currentUser == null || string.IsNullOrEmpty(_currentUser.Token))
        {
            return null;
        }

        try
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _currentUser.Token);

            var response = await _httpClient.GetAsync("auth/me");
            if (!response.IsSuccessStatusCode)
            {
                return _currentUser;
            }

            var content = await response.Content.ReadAsStringAsync();
            var user = JsonSerializer.Deserialize<CurrentUserResponse>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (user == null) return _currentUser;

            _currentUser = _currentUser with
            {
                Name = string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName,
                Position = string.IsNullOrWhiteSpace(user.Position) ? _currentUser.Position : user.Position
            };

            return _currentUser;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh current user");
            return _currentUser;
        }
    }

    /// <summary>
    /// Logout
    /// </summary>
    public async Task LogoutAsync()
    {
        try
        {
            await _httpClient.PostAsync("auth/logout", null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Logout error");
        }
        finally
        {
            _currentUser = null;
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
    }

    /// <summary>
    /// Get current user
    /// </summary>
    public AuthResult? GetCurrentUser() => _currentUser;

    /// <summary>
    /// Get Assess Token
    /// </summary>
    public string? GetAccessToken() => _currentUser?.Token;
}