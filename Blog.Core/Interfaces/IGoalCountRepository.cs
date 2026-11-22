using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

/// <summary>Daily goal counters (Admin → Analytics → Goals). Numbers only: no reader is identified by a row.</summary>
public interface IGoalCountRepository
{
    /// <summary>Adds each hit to its day's counter, creating the row when it is the first of the day.</summary>
    Task AddAsync(IReadOnlyCollection<GoalHit> hits);
    /// <summary>Total per goal key over the days given, both ends included.</summary>
    Task<Dictionary<string, int>> GetTotalsAsync(Guid ownerId, DateTime fromDay, DateTime toDay);
    /// <summary>Every day's count for every goal over the days given, both ends included. At most a year of days times a few dozen goals.</summary>
    Task<List<GoalHit>> GetDailyAsync(Guid ownerId, DateTime fromDay, DateTime toDay);
    /// <summary>Removes a goal's history when the goal itself is removed. Returns the rows deleted.</summary>
    Task<int> DeleteGoalAsync(Guid ownerId, string goalKey);
}
