using Microsoft.EntityFrameworkCore;
using TheWell.Core.Entities;

namespace TheWell.Data.Repositories;

public class UserRepository(WellDbContext db)
{
    public async Task<User?> FindByEidAsync(string eidHash) =>
        await db.Users.FirstOrDefaultAsync(u => u.UniversityEIDHash == eidHash);

    public async Task<User?> FindByIdAsync(Guid id) =>
        await db.Users.FindAsync(id);

    public async Task<List<User>> GetAllAsync() =>
        await db.Users.ToListAsync();

    public async Task AddAsync(User user)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }

    public async Task<User?> FindByEmailHashAsync(string emailHash) =>
        await db.Users.FirstOrDefaultAsync(u => u.EmailHash == emailHash);

    public async Task<User?> FindByRefreshTokenHashAsync(string hash) =>
        await db.Users.FirstOrDefaultAsync(u => u.RefreshTokenHash == hash);

    public async Task<User?> FindByPasswordResetTokenHashAsync(string hash) =>
        await db.Users.FirstOrDefaultAsync(u => u.PasswordResetTokenHash == hash);

    public async Task SaveAsync() => await db.SaveChangesAsync();

    public async Task DeleteAsync(User user)
    {
        db.Users.Remove(user);
        await db.SaveChangesAsync();
    }
}
