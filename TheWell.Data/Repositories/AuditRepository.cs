using Microsoft.EntityFrameworkCore;
using TheWell.Core.Entities;

namespace TheWell.Data.Repositories;

public class AuditRepository(WellDbContext db)
{
    public async Task AddAsync(AuthenticationAudit audit)
    {
        db.AuthenticationAudits.Add(audit);
        await db.SaveChangesAsync();
    }

    public async Task<AuthenticationAudit?> GetLatestOtpAsync(Guid userId) =>
        await db.AuthenticationAudits
                .Where(a => a.UserID == userId && a.Action == AuditActions.OtpRequest && a.OtpHash != null)
                .OrderByDescending(a => a.AttemptTimestamp)
                .FirstOrDefaultAsync();

    public async Task<int> CountRecentOtpFailuresAsync(Guid userId)
    {
        // Count OtpFailed entries since the user's last successful OTP request — resets the window on new code
        var lastRequest = await db.AuthenticationAudits
            .Where(a => a.UserID == userId && a.Action == AuditActions.OtpRequest)
            .OrderByDescending(a => a.AttemptTimestamp)
            .Select(a => a.AttemptTimestamp)
            .FirstOrDefaultAsync();

        return await db.AuthenticationAudits
            .Where(a => a.UserID == userId
                        && a.Action == AuditActions.OtpFailed
                        && a.AttemptTimestamp > lastRequest)
            .CountAsync();
    }

    public async Task<List<AuthenticationAudit>> GetAllAsync() =>
        await db.AuthenticationAudits
                .OrderByDescending(a => a.AttemptTimestamp)
                .ToListAsync();
}
