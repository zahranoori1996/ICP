using Application.DTOs;
using Application.Interface;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Services;

/// <summary>
/// Implementation of user management service
/// </summary>
public class UserManagementService : IUserManagementService
{
    private readonly IsatisDbContext _context;
    private readonly ILogger<UserManagementService> _logger;

    public UserManagementService(IsatisDbContext context, ILogger<UserManagementService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get all users
    /// </summary>
    public async Task<List<UserListDto>> GetAllUsersAsync()
    {
        try
        {
            var users = await _context.Users
                .Select(u => new UserListDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    FullName = u.FullName,
                    Position = u.Position
                })
                .ToListAsync();

            _logger.LogInformation("Retrieved {UserCount} users", users.Count);
            return users;
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
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("User with ID {UserId} not found", userId);
                return null;
            }

            return new UserResponseDto
            {
                Id = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                Position = user.Position
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user with ID {UserId}", userId);
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
            // Check if username already exists
            if (await UsernameExistsAsync(createUserDto.Username))
            {
                _logger.LogWarning("Username {Username} already exists", createUserDto.Username);
                return (false, "Username already exists", null);
            }

            // Validate input
            if (string.IsNullOrWhiteSpace(createUserDto.Username))
                return (false, "Username cannot be empty.", null);

            if (string.IsNullOrWhiteSpace(createUserDto.Password))
                return (false, "Password cannot be empty.", null);

            // Create new user
            var newUser = new Users
            {
                Id = Guid.NewGuid(),
                Username = createUserDto.Username.ToLower().Trim(),
                FullName = createUserDto.FullName ?? createUserDto.Username,
                Position = createUserDto.Position ?? "User",
                PasswordHash = HashPassword(createUserDto.Password)
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            _logger.LogInformation("New user created: {Username}", newUser.Username);

            var responseDto = new UserResponseDto
            {
                Id = newUser.Id,
                Username = newUser.Username,
                FullName = newUser.FullName,
                Position = newUser.Position
            };

            return (true, "User created successfully.", responseDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating new user");
            return (false, "Error creating user", null);
        }
    }

    /// <summary>
    /// Update user password
    /// </summary>
    public async Task<(bool Success, string Message)> UpdateUserPasswordAsync(Guid userId, string newPassword)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(newPassword))
                return (false, "Password cannot be empty.");

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return (false, "User not found");

            user.PasswordHash = HashPassword(newPassword);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Password updated for user: {Username}", user.Username);
            return (true, "Password changed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user password for user ID {UserId}", userId);
            return (false, "Error changing password");
        }
    }

    /// <summary>
    /// Delete user
    /// </summary>
    public async Task<(bool Success, string Message)> DeleteUserAsync(Guid userId)
    {
        try
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return (false, "User not found");

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation("User deleted: {Username}", user.Username);
            return (true, "User successfully deleted.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user with ID {UserId}", userId);
            return (false, "Error deleting user");
        }
    }

    /// <summary>
    /// Check if username exists
    /// </summary>
    public async Task<bool> UsernameExistsAsync(string username)
    {
        return await _context.Users
            .AnyAsync(u => u.Username.ToLower() == username.ToLower());
    }

    /// <summary>
    /// Authenticate user with username and password
    /// </summary>
    public async Task<UserResponseDto?> AuthenticateAsync(string username, string password)
    {
        try
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

            if (user == null)
            {
                _logger.LogWarning("Authentication failed: user {Username} not found", username);
                return null;
            }

            // Verify password
            if (!VerifyPassword(password, user.PasswordHash))
            {
                _logger.LogWarning("Authentication failed: invalid password for user {Username}", username);
                return null;
            }

            _logger.LogInformation("User authenticated successfully: {Username}", username);

            return new UserResponseDto
            {
                Id = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                Position = user.Position
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error authenticating user {Username}", username);
            return null;
        }
    }

    /// <summary>
    /// Hash a password using PBKDF2
    /// </summary>
    private static string HashPassword(string password)
    {
        const int iterations = 10000;
        const int saltLength = 16;
        const int hashLength = 32; // طول هش

        // Generate random salt
        byte[] salt = new byte[saltLength];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }

        // اصلاح شده: استفاده از متد استاتیک Pbkdf2 به جای سازنده قدیمی
        // نکته مهم: برای سازگاری با کدهای قبلی حتما از SHA1 استفاده کنید
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA1,
            hashLength);

        // Combine salt and hash for storage
        byte[] hashWithSalt = new byte[salt.Length + hash.Length];
        Buffer.BlockCopy(salt, 0, hashWithSalt, 0, salt.Length);
        Buffer.BlockCopy(hash, 0, hashWithSalt, salt.Length, hash.Length);

        return Convert.ToBase64String(hashWithSalt);
    }

    /// <summary>
    /// Verify a password against its hash
    /// </summary>
    private static bool VerifyPassword(string password, string hash)
    {
        try
        {
            const int saltLength = 16;
            const int iterations = 10000;
            const int hashLength = 32;

            // Decode the hash
            byte[] hashWithSalt = Convert.FromBase64String(hash);

            // Extract salt
            byte[] salt = new byte[saltLength];
            Buffer.BlockCopy(hashWithSalt, 0, salt, 0, saltLength);

            // اصلاح شده: استفاده از متد استاتیک Pbkdf2
            byte[] computedHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations,
                HashAlgorithmName.SHA1,
                hashLength);

            // Compare hashes
            for (int i = 0; i < computedHash.Length; i++)
            {
                if (hashWithSalt[salt.Length + i] != computedHash[i])
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
