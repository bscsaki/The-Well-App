using System.ComponentModel.DataAnnotations;

namespace TheWell.Core.DTOs;

public record SubmitIntakeRequest(
    [Required, MaxLength(500)] string MyHabit,
    [Required, MaxLength(500)] string MyGoal,
    [Required, MaxLength(500)] string IAmPersonWho,
    [Required, MaxLength(500)] string Strategy1,
    [Required, MaxLength(500)] string Strategy2,
    [Required, MaxLength(500)] string ToImproveMyselfIWill,
    [Required, MaxLength(500)] string RewardMyselfWith,
    [Required, MaxLength(500)] string PeopleForEncouragement);

public record IntakeResponse(
    string MyHabit,
    string MyGoal,
    string IAmPersonWho,
    string Strategy1,
    string Strategy2,
    string ToImproveMyselfIWill,
    string RewardMyselfWith,
    string PeopleForEncouragement,
    bool IsUnlocked,
    DateTime? CompletedAt);
