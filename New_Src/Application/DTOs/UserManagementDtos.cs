using System.Text.Json.Serialization;

namespace Application.DTOs;

/// <summary>
/// DTO for user list response
/// </summary>
public class UserListDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public string Position { get; set; } = "User";
}

/// <summary>
/// DTO for creating a new user
/// </summary>
public class CreateUserDto
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public string Position { get; set; } = "User";
}

/// <summary>
/// DTO for updating user password
/// </summary>
public class UpdateUserPasswordDto
{
    [JsonPropertyName("userId")]
    public Guid UserId { get; set; }

    [JsonPropertyName("newPassword")]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>
/// DTO for user response
/// </summary>
public class UserResponseDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public string Position { get; set; } = "User";
}

/// <summary>
/// DTO for operation response
/// </summary>
public class OperationResultDto
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
