using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;
using System.Text.Json;

namespace Blog.Infrastructure.Data.Repositories;

public class SettingRepository : ISettingRepository
{
    private readonly DapperContext _ctx;
    public SettingRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<UserSettings> GetSettingsAsync(Guid userId)
    {
        using var conn = _ctx.CreateConnection();
        var json = await conn.ExecuteScalarAsync<string>(
            "SELECT JsonPayload FROM Settings WHERE UserId = @UserId", 
            new { UserId = userId });

        if (string.IsNullOrEmpty(json))
        {
            // If they have no settings row yet, return defaults
            return new UserSettings { UserId = userId };
        }

        try
        {
            var settings = JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
            settings.UserId = userId; // Ensure ID matches perfectly
            return settings;
        }
        catch
        {
            return new UserSettings { UserId = userId };
        }
    }

    public async Task SaveSettingsAsync(Guid userId, UserSettings settings)
    {
        settings.UserId = userId; // Force sync
        var json = JsonSerializer.Serialize(settings);
        
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            MERGE Settings AS target
            USING (SELECT @UserId AS UserId, @JsonPayload AS JsonPayload, GETUTCDATE() AS UpdatedAt) AS source
            ON target.UserId = source.UserId
            WHEN MATCHED THEN
                UPDATE SET JsonPayload = source.JsonPayload, UpdatedAt = source.UpdatedAt
            WHEN NOT MATCHED THEN
                INSERT (UserId, JsonPayload, UpdatedAt) VALUES (source.UserId, source.JsonPayload, source.UpdatedAt);",
            new { UserId = userId, JsonPayload = json });
    }

    public async Task<bool> IsInstalledAsync()
    {
        using var conn = _ctx.CreateConnection();
        // Just verify if any settings row exists, implying installation
        var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM Settings");
        return count > 0;
    }

    public async Task DismissOnboardingAsync(Guid userId)
    {
        var settings = await GetSettingsAsync(userId);
        settings.OnboardingDismissed = true;
        await SaveSettingsAsync(userId, settings);
    }

    public async Task CompleteOnboardingTaskAsync(Guid userId, string taskId)
    {
        var settings = await GetSettingsAsync(userId);
        if (!settings.OnboardingCompletedTasks.Contains(taskId))
        {
            settings.OnboardingCompletedTasks.Add(taskId);
            await SaveSettingsAsync(userId, settings);
        }
    }

    // Keep legacy interface signatures briefly so DI doesn't instantly crash while we find references, but return default dummy strings
    public async Task<string?> GetAsync(string key) => null;
    public async Task<Dictionary<string, string>> GetGroupAsync(string group) => new Dictionary<string, string>();
    public async Task SetAsync(string key, string? value, string group = "general") { await Task.CompletedTask; }
    public async Task SetManyAsync(Dictionary<string, string?> settings, string group = "general") { await Task.CompletedTask; }
}
