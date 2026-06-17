using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TheWell.API.Services;
using TheWell.Core.DTOs;
using TheWell.Core.Entities;
using TheWell.Core.Interfaces;
using TheWell.Data.Repositories;

namespace TheWell.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    UserRepository userRepo,
    AuditRepository auditRepo,
    IntakeRepository intakeRepo,
    IEncryptionService encryption,
    IOtpService otpService,
    IEmailService emailService,
    TokenService tokenService) : ControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var eidHash = encryption.Hash(request.ENumber);
        var user = await userRepo.FindByEidAsync(eidHash);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            if (user is not null)
                await auditRepo.AddAsync(new AuthenticationAudit
                    { UserID = user.UserID, Action = AuditActions.FailedLogin });
            return Unauthorized(new { error = "Invalid credentials" });
        }

        if (user.AccountStatus == AccountStatuses.Suspended)
            return Forbid();

        await auditRepo.AddAsync(new AuthenticationAudit
            { UserID = user.UserID, Action = AuditActions.Login });

        var intake = await GetIntakeStatus(user.UserID);

        var refreshToken = tokenService.GenerateRefreshToken();
        user.RefreshTokenHash = tokenService.HashRefreshToken(refreshToken);
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(TokenService.RefreshTokenDays);
        await userRepo.SaveAsync();

        return Ok(new LoginResponse(
            tokenService.GenerateAccessToken(user),
            refreshToken,
            user.IsPasswordResetRequired,
            intake,
            user.AccountStatus));
    }

    /// <summary>First-login password change. Requires JWT — user is identified from the token, not the body.</summary>
    [HttpPost("force-reset")]
    [Authorize]
    public async Task<IActionResult> ForceReset([FromBody] ForceResetRequest request)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var user = await userRepo.FindByIdAsync(userId);
        if (user is null) return NotFound();
        if (!user.IsPasswordResetRequired) return BadRequest(new { error = "Password reset not required" });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.IsPasswordResetRequired = false;
        user.AccountStatus = AccountStatuses.Active;
        await userRepo.SaveAsync();

        await auditRepo.AddAsync(new AuthenticationAudit
            { UserID = user.UserID, Action = AuditActions.PasswordReset });

        return Ok(new { message = "Password updated successfully" });
    }

    /// <summary>OTP-flow password reset. Token from VerifyOtp replaces the JWT.</summary>
    [HttpPost("password-reset")]
    [EnableRateLimiting("auth-password-reset")]
    public async Task<IActionResult> PasswordReset([FromBody] PasswordResetRequest request)
    {
        var hash = tokenService.HashPasswordResetToken(request.Token);
        var user = await userRepo.FindByPasswordResetTokenHashAsync(hash);

        if (user is null || user.PasswordResetTokenExpiresAt < DateTime.UtcNow)
            return BadRequest(new { error = "Invalid or expired reset token" });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.IsPasswordResetRequired = false;
        user.AccountStatus = AccountStatuses.Active;
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresAt = null;
        await userRepo.SaveAsync();

        await auditRepo.AddAsync(new AuthenticationAudit
            { UserID = user.UserID, Action = AuditActions.PasswordReset });

        return Ok(new { message = "Password updated successfully" });
    }

    [HttpPost("otp/request")]
    [EnableRateLimiting("auth-otp-request")]
    public async Task<IActionResult> RequestOtp([FromBody] OtpRequestDto request)
    {
        var emailHash = encryption.Hash(request.Email);
        var user = await userRepo.FindByEmailHashAsync(emailHash);
        if (user is null) return Ok(new { message = "If that email exists, a code was sent" });

        var otp = otpService.Generate();
        var hash = otpService.Hash(otp);
        var expiry = DateTime.UtcNow.AddMinutes(15);

        await auditRepo.AddAsync(new AuthenticationAudit
        {
            UserID = user.UserID,
            Action = AuditActions.OtpRequest,
            OtpHash = hash,
            OtpExpiresAt = expiry
        });

        await emailService.SendOtpAsync(request.Email, otp);
        return Ok(new { message = "If that email exists, a code was sent" });
    }

    [HttpPost("otp/verify")]
    [EnableRateLimiting("auth-otp-verify")]
    public async Task<IActionResult> VerifyOtp([FromBody] OtpVerifyRequest request)
    {
        var emailHash = encryption.Hash(request.Email);
        var user = await userRepo.FindByEmailHashAsync(emailHash);
        if (user is null) return BadRequest(new { error = "Invalid code" });

        var latestOtp = await auditRepo.GetLatestOtpAsync(user.UserID);
        if (latestOtp?.OtpHash is null || latestOtp.OtpExpiresAt is null)
            return BadRequest(new { error = "No active OTP found" });

        var failures = await auditRepo.CountRecentOtpFailuresAsync(user.UserID);
        if (failures >= 5)
            return StatusCode(429, new { error = "Too many failed attempts. Request a new code." });

        if (!otpService.Verify(request.Otp, latestOtp.OtpHash, latestOtp.OtpExpiresAt.Value))
        {
            await auditRepo.AddAsync(new AuthenticationAudit
                { UserID = user.UserID, Action = AuditActions.OtpFailed });
            return BadRequest(new { error = "Invalid or expired code" });
        }

        await auditRepo.AddAsync(new AuthenticationAudit
            { UserID = user.UserID, Action = AuditActions.OtpVerify });

        var resetToken = tokenService.GeneratePasswordResetToken();
        user.IsPasswordResetRequired = true;
        user.PasswordResetTokenHash = tokenService.HashPasswordResetToken(resetToken);
        user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(15);
        await userRepo.SaveAsync();

        return Ok(new OtpVerifyResponse(resetToken));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var hash = tokenService.HashRefreshToken(request.RefreshToken);
        var user = await userRepo.FindByRefreshTokenHashAsync(hash);

        if (user is null || user.RefreshTokenExpiresAt < DateTime.UtcNow)
            return Unauthorized(new { error = "Invalid or expired refresh token" });

        if (user.AccountStatus == AccountStatuses.Suspended)
            return Forbid();

        // Rotate the refresh token on every use
        var newRefreshToken = tokenService.GenerateRefreshToken();
        user.RefreshTokenHash = tokenService.HashRefreshToken(newRefreshToken);
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(TokenService.RefreshTokenDays);
        await userRepo.SaveAsync();

        return Ok(new
        {
            accessToken = tokenService.GenerateAccessToken(user),
            refreshToken = newRefreshToken
        });
    }

    private async Task<bool> GetIntakeStatus(Guid userId)
    {
        var intake = await intakeRepo.GetByUserAsync(userId);
        return intake is not null;
    }
}
