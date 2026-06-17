using TheWell.Core.Entities;

namespace TheWell.Core.Interfaces;

public interface IStreakService
{
    Task<int> CalculateAsync(Guid userId);
    int Calculate(IEnumerable<DailyLog> logs);
}
