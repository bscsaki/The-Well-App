using System.ComponentModel.DataAnnotations;

namespace TheWell.Core.DTOs;

public record DailyLogResponse(
    Guid LogID,
    DateOnly LogDate,
    bool IsCompleted,
    string? Note,
    DateTime CreatedAt,
    bool IsLocked);

public record CreateLogRequest(
    [Required] DateOnly LogDate,
    bool IsCompleted,
    [MaxLength(1000)] string? Note);

public record UpdateLogRequest(
    bool IsCompleted,
    [MaxLength(1000)] string? Note);
