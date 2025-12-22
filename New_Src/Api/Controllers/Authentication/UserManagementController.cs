using Application.DTOs;
using Application.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers.Authentication;

/// <summary>
/// Handles user management operations (admin only)
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UserManagementController : ControllerBase
{
    private readonly IUserManagementService _userManagementService;
    private readonly ILogger<UserManagementController> _logger;

    public UserManagementController(IUserManagementService userManagementService, ILogger<UserManagementController> logger)
    {
        _userManagementService = userManagementService;
        _logger = logger;
    }

    /// <summary>
    /// Get all users (Admin only)
    /// </summary>
    [HttpGet("all")]
    [Authorize(Roles = "Admin,admin")]
    public async Task<ActionResult<List<UserListDto>>> GetAllUsers()
    {
        try
        {
            var users = await _userManagementService.GetAllUsersAsync();
            _logger.LogInformation("Retrieved all users list");
            return Ok(users);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving users");
            return StatusCode(500, new { message = "Error loading user list" });
        }
    }

    /// <summary>
    /// Get user by ID (Admin only)
    /// </summary>
    [HttpGet("{userId}")]
    [Authorize(Roles = "Admin,admin")]
    public async Task<ActionResult<UserResponseDto>> GetUserById(Guid userId)
    {
        try
        {
            var user = await _userManagementService.GetUserByIdAsync(userId);
            if (user == null)
                return NotFound(new { message = "User not found" });

            return Ok(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user {UserId}", userId);
            return StatusCode(500, new { message = "Error loading user information" });
        }
    }

    /// <summary>
    /// Create a new user (Admin only)
    /// </summary>
    [HttpPost("create")]
    [Authorize(Roles = "Admin,admin")]
    public async Task<ActionResult<object>> CreateUser([FromBody] CreateUserDto createUserDto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(createUserDto.Username))
                return BadRequest(new { success = false, message = "Username is required" });

            if (string.IsNullOrWhiteSpace(createUserDto.Password))
                return BadRequest(new { success = false, message = "Password is required" });

            var (success, message, user) = await _userManagementService.CreateUserAsync(createUserDto);

            if (!success)
                return BadRequest(new { success = false, message });

            _logger.LogInformation("New user created: {Username}", createUserDto.Username);
            return Ok(new { success = true, message, user });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user");
            return StatusCode(500, new { success = false, message = "Error creating user" });
        }
    }

    /// <summary>
    /// Update user password (Admin only)
    /// </summary>
    [HttpPost("change-password")]
    [Authorize(Roles = "Admin,admin")]
    public async Task<ActionResult<object>> UpdateUserPassword([FromBody] UpdateUserPasswordDto updateDto)
    {
        try
        {
            if (updateDto.UserId == Guid.Empty)
                return BadRequest(new { success = false, message = "User ID is required" });

            if (string.IsNullOrWhiteSpace(updateDto.NewPassword))
                return BadRequest(new { success = false, message = "New password required" });

            var (success, message) = await _userManagementService.UpdateUserPasswordAsync(updateDto.UserId, updateDto.NewPassword);

            if (!success)
                return BadRequest(new { success = false, message });

            _logger.LogInformation("Password updated for user ID: {UserId}", updateDto.UserId);
            return Ok(new { success = true, message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating password for user {UserId}", updateDto.UserId);
            return StatusCode(500, new { success = false, message = "Error changing password" });
        }
    }

    /// <summary>
    /// Delete user (Admin only)
    /// </summary>
    [HttpDelete("{userId}")]
    [Authorize(Roles = "Admin,admin")]
    public async Task<ActionResult<object>> DeleteUser(Guid userId)
    {
        try
        {
            var (success, message) = await _userManagementService.DeleteUserAsync(userId);

            if (!success)
                return BadRequest(new { success = false, message });

            _logger.LogInformation("User deleted: {UserId}", userId);
            return Ok(new { success = true, message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user {UserId}", userId);
            return StatusCode(500, new { success = false, message = "Error deleting user" });
        }
    }

 
    [HttpPost("change-my-password")]
    [Authorize]
    public async Task<IActionResult> ChangeMyPassword([FromBody] ChangeMyPasswordDto dto)
    {
        var username = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(username))
            return Unauthorized(new { message = "The token is invalid." });

        var user = await _userManagementService.AuthenticateAsync(username, dto.CurrentPassword);
        if (user == null)
            return BadRequest(new { message = "The current password is incorrect." });

        var (success, message) = await _userManagementService.UpdateUserPasswordAsync(user.Id, dto.NewPassword);

        if (!success)
            return BadRequest(new { message });

        return Ok(new { message = "Password changed successfully" });
    }

}
