using System.ComponentModel.DataAnnotations;

namespace TheWell.Core.DTOs;

public record LoginRequest(
    [Required, MaxLength(20)] string ENumber,
    [Required, MaxLength(128)] string Password);

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    bool IsPasswordResetRequired,
    bool IsIntakeComplete,
    string AccountStatus);

public record ForceResetRequest(
    [Required, MinLength(8), MaxLength(128)] string NewPassword);

public record PasswordResetRequest(
    [Required] string Token,
    [Required, MinLength(8), MaxLength(128)] string NewPassword);

public record OtpRequestDto(
    [Required, EmailAddress, MaxLength(254)] string Email);

public record OtpVerifyRequest(
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, StringLength(6, MinimumLength = 6)] string Otp);

public record OtpVerifyResponse(string PasswordResetToken);

public record RefreshRequest(
    [Required] string RefreshToken);
