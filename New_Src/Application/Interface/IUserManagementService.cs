using Application.DTOs;

namespace Application.Interface;

/// <summary>
/// Interface for user management operations
/// </summary>
public interface IUserManagementService
{
    /// <summary>
    /// Get all users
    /// </summary>
    Task<List<UserListDto>> GetAllUsersAsync();

    /// <summary>
    /// Get user by ID
    /// </summary>
    Task<UserResponseDto?> GetUserByIdAsync(Guid userId);

    /// <summary>
    /// Create a new user
    /// </summary>
    Task<(bool Success, string Message, UserResponseDto? User)> CreateUserAsync(CreateUserDto createUserDto);

    /// <summary>
    /// Update user password
    /// </summary>
    Task<(bool Success, string Message)> UpdateUserPasswordAsync(Guid userId, string newPassword);

    /// <summary>
    /// Delete user
    /// </summary>
    Task<(bool Success, string Message)> DeleteUserAsync(Guid userId);

    /// <summary>
    /// Check if username exists
    /// </summary>
    Task<bool> UsernameExistsAsync(string username);

    /// <summary>
    /// Authenticate user with username and password
    /// </summary>
    Task<UserResponseDto?> AuthenticateAsync(string username, string password);
}
