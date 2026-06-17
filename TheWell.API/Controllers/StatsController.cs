using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheWell.Core.DTOs;
using TheWell.Core.Interfaces;
using TheWell.Data.Repositories;

namespace TheWell.API.Controllers;

[ApiController]
[Route("api/stats")]
[Authorize]
public class StatsController(DailyLogRepository logRepo, IStreakService streakService) : ControllerBase
{
    private Guid CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub"), out var id)
            ? id
            : throw new UnauthorizedAccessException();

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var logs = await logRepo.GetByUserAsync(CurrentUserId);
        var totalCompleted = logs.Count(l => l.IsCompleted);
        var streak = streakService.Calculate(logs);
        var wellFill = Math.Min(totalCompleted / 60.0, 1.0);

        return Ok(new StatsResponse(totalCompleted, streak, wellFill));
    }
}
