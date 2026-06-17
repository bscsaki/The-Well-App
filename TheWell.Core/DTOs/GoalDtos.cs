namespace TheWell.Core.DTOs;

public record GoalResponse(
    Guid GoalID,
    string GoalDefinition,
    DateTime CreatedAt,
    bool IsLocked);

public record CreateGoalRequest(string GoalDefinition);

public record UpdateGoalRequest(string GoalDefinition);
