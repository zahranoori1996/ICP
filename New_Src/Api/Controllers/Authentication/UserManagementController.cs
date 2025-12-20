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
            return StatusCode(500, new { message = "خطا در بارگذاری لیست کاربران" });
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
                return NotFound(new { message = "کاربر یافت نشد" });

            return Ok(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user {UserId}", userId);
            return StatusCode(500, new { message = "خطا در بارگذاری اطلاعات کاربر" });
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
                return BadRequest(new { success = false, message = "نام کاربری الزامی است" });

            if (string.IsNullOrWhiteSpace(createUserDto.Password))
                return BadRequest(new { success = false, message = "رمز عبور الزامی است" });

            var (success, message, user) = await _userManagementService.CreateUserAsync(createUserDto);

            if (!success)
                return BadRequest(new { success = false, message });

            _logger.LogInformation("New user created: {Username}", createUserDto.Username);
            return Ok(new { success = true, message, user });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user");
            return StatusCode(500, new { success = false, message = "خطا در ایجاد کاربر" });
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
                return BadRequest(new { success = false, message = "شناسه کاربر الزامی است" });

            if (string.IsNullOrWhiteSpace(updateDto.NewPassword))
                return BadRequest(new { success = false, message = "رمز عبور جدید الزامی است" });

            var (success, message) = await _userManagementService.UpdateUserPasswordAsync(updateDto.UserId, updateDto.NewPassword);

            if (!success)
                return BadRequest(new { success = false, message });

            _logger.LogInformation("Password updated for user ID: {UserId}", updateDto.UserId);
            return Ok(new { success = true, message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating password for user {UserId}", updateDto.UserId);
            return StatusCode(500, new { success = false, message = "خطا در تغییر رمز عبور" });
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
            return StatusCode(500, new { success = false, message = "خطا در حذف کاربر" });
        }
    }
}
