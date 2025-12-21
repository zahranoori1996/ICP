using Application.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Api.Controllers;

/// <summary>
/// Handles user authentication, registration, and session management.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;
    private readonly IUserManagementService _userManagementService;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthController"/> class.
    /// </summary>
    /// <param name="configuration">The configuration settings.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="userManagementService">The user management service.</param>
    public AuthController(IConfiguration configuration, ILogger<AuthController> logger, IUserManagementService userManagementService)
    {
        _configuration = configuration;
        _logger = logger;
        _userManagementService = userManagementService;
    }

    /// <summary>
    /// Authenticates a user and generates a JWT token.
    /// </summary>
    /// <param name="request">The login request containing username and password.</param>
    /// <returns>A login response with the JWT token if successful.</returns>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> LoginAsync([FromBody] LoginRequest request)
    {
        _logger.LogInformation("Login attempt for user: {Username}", request.Username);

        // Authenticate user from database
        var user = await _userManagementService.AuthenticateAsync(request.Username, request.Password);

        if (user == null)
        {
            _logger.LogWarning("Failed login attempt for user: {Username}", request.Username);
            return Unauthorized(new LoginResponse(false, "Invalid username or password"));
        }

        var token = GenerateJwtToken(user.Username, user.FullName, user.Position);

        _logger.LogInformation("Successful login for user: {Username}", request.Username);

        return Ok(new LoginResponse(
            IsAuthenticated: true,
            Message: "Login successful",
            Name: user.FullName,
            Token: token,
            Position: user.Position
        ));
    }

    /// <summary>
    /// Registers a new user.
    /// </summary>
    /// <param name="request">The registration details.</param>
    /// <returns>A response indicating the result of the registration.</returns>
    [HttpPost("register")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<RegisterResponse>> RegisterAsync([FromBody] RegisterRequest request)
    {
        if (await _userManagementService.UsernameExistsAsync(request.Username))
        {
            return Conflict(new RegisterResponse(false, "Username already exists"));
        }

        var (success, message, userDto) = await _userManagementService.CreateUserAsync(new Application.DTOs.CreateUserDto
        {
            Username = request.Username,
            Password = request.Password,
            FullName = request.FullName ?? request.Username,
            Position = request.Position ?? "User"
        });

        if (!success)
        {
            return BadRequest(new RegisterResponse(false, message));
        }

        _logger.LogInformation("New user registered: {Username}", request.Username);

        return Ok(new RegisterResponse(true, "Registration successful"));
    }

    /// <summary>
    /// Retrieves the current authenticated user's information.
    /// </summary>
    /// <returns>The user information details.</returns>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserInfo>> GetCurrentUserAsync()
    {
        var username = User.FindFirst(ClaimTypes.Name)?.Value;
        var fullName = User.FindFirst(ClaimTypes.GivenName)?.Value;
        var position = User.FindFirst(ClaimTypes.Role)?.Value;

        if (string.IsNullOrEmpty(username))
            return Unauthorized();

        return Ok(new UserInfo(username, fullName ?? username, position ?? "User"));
    }

    /// <summary>
    /// Checks if there is an active session.
    /// </summary>
    /// <returns>The session check result.</returns>
    [HttpGet("check-session")]
    [AllowAnonymous]
    public ActionResult CheckSession()
    {
        return Ok(new LoginResponse(false, "No active session"));
    }

    /// <summary>
    /// Logs out the current user.
    /// </summary>
    /// <returns>A message indicating logout success.</returns>
    [HttpPost("logout")]
    [AllowAnonymous]
    public ActionResult Logout()
    {
        return Ok(new { Message = "Logged out successfully" });
    }

    /// <summary>
    /// Generates a JWT token for the specified user.
    /// </summary>
    private string GenerateJwtToken(string username, string fullName, string position)
    {
        var jwtSettings = _configuration.GetSection("Jwt");
        var secret = jwtSettings["Secret"] ?? "IsatisICP-SuperSecret-Key-2024-Must-Be-At-Least-32-Characters!";
        var issuer = jwtSettings["Issuer"] ?? "IsatisICP";
        var audience = jwtSettings["Audience"] ?? "IsatisICP-Users";
        var expiryMinutes = int.Parse(jwtSettings["AccessTokenExpiryMinutes"] ?? "60");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.GivenName, fullName),
            new Claim(ClaimTypes.Role, position),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public record LoginRequest(string Username, string Password, bool RememberMe = false);
public record LoginResponse(bool IsAuthenticated, string Message, string Name = "", string Token = "", string Position = "");
public record RegisterRequest(string Username, string Password, string? FullName, string? Position);
public record RegisterResponse(bool Succeeded, string Message);
public record UserInfo(string Username, string FullName, string Position);
public record UserRecord(string Username, string Password, string FullName, string Position);