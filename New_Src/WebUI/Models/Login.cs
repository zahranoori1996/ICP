using System.ComponentModel.DataAnnotations;

namespace WebUI.Models;

/// <summary>
/// مدل درخواست لاگین برای ارسال به API
/// </summary>
public record LoginRequest(
    [Required(ErrorMessage = "UserName is required.")]
    //[EmailAddress]
    string UserName,

    [Required(ErrorMessage = "Password is required.")]
    string Password);

/// <summary>
/// مدل نتیجه احراز هویت بازگشتی از API
/// </summary>
public record AuthResult(
    bool IsAuthenticated,
    string Message,
    string Token = "",
    string Name = "Guest");