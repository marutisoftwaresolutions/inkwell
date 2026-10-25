using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

/// <summary>Contact forms and submissions (ADR-0030). Every statement is scoped to the owner; search input is escaped.</summary>
public sealed class FormRepository : IFormRepository
{
    private readonly DapperContext _ctx;
    public FormRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<IReadOnlyList<ContactForm>> ListAsync(Guid ownerId)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<ContactForm>(@"
            SELECT f.*, ISNULL(s.Total, 0) AS SubmissionCount, ISNULL(s.Unread, 0) AS UnreadCount, s.LastAt AS LastSubmissionAt
            FROM Forms f
            OUTER APPLY (SELECT COUNT(*) AS Total, SUM(CASE WHEN ReadAt IS NULL THEN 1 ELSE 0 END) AS Unread, MAX(CreatedAt) AS LastAt
                         FROM FormSubmissions WHERE FormId = f.Id) s
            WHERE f.OwnerId = @ownerId
            ORDER BY f.Name", new { ownerId })).ToList();
    }

    public async Task<ContactForm?> GetAsync(Guid id, Guid ownerId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ContactForm>(@"
            SELECT f.*, (SELECT COUNT(*) FROM FormSubmissions WHERE FormId = f.Id) AS SubmissionCount
            FROM Forms f WHERE f.Id = @id AND f.OwnerId = @ownerId", new { id, ownerId });
    }

    public async Task<ContactForm?> GetByKeyAsync(string formKey, Guid ownerId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ContactForm>(
            "SELECT * FROM Forms WHERE FormKey = @formKey AND OwnerId = @ownerId", new { formKey, ownerId });
    }

    public async Task<bool> KeyExistsAsync(string formKey, Guid ownerId, Guid? exceptId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Forms WHERE FormKey = @formKey AND OwnerId = @ownerId AND (@exceptId IS NULL OR Id <> @exceptId)",
            new { formKey, ownerId, exceptId }) > 0;
    }

    public async Task CreateAsync(ContactForm f)
    {
        if (f.Id == Guid.Empty) f.Id = Guid.NewGuid();
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO Forms (Id, OwnerId, FormKey, Name, FieldsJson, SubmitLabel, SuccessMessage, NotifyEmail, RetentionDays, IsActive, CreatedAt, UpdatedAt)
            VALUES (@Id, @OwnerId, @FormKey, @Name, @FieldsJson, @SubmitLabel, @SuccessMessage, @NotifyEmail, @RetentionDays, @IsActive, SYSUTCDATETIME(), SYSUTCDATETIME())", f);
    }

    public async Task<bool> UpdateAsync(ContactForm f)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteAsync(@"
            UPDATE Forms SET FormKey = @FormKey, Name = @Name, FieldsJson = @FieldsJson, SubmitLabel = @SubmitLabel,
                   SuccessMessage = @SuccessMessage, NotifyEmail = @NotifyEmail, RetentionDays = @RetentionDays,
                   IsActive = @IsActive, UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @Id AND OwnerId = @OwnerId", f) > 0;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteAsync("DELETE FROM Forms WHERE Id = @id AND OwnerId = @ownerId", new { id, ownerId }) > 0;   // submissions cascade
    }

    public async Task<long> AddSubmissionAsync(FormSubmission s)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteScalarAsync<long>(@"
            INSERT INTO FormSubmissions (FormId, OwnerId, ValuesJson, SearchText, AddressHash, ContentHash, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@FormId, @OwnerId, @ValuesJson, @SearchText, @AddressHash, @ContentHash, SYSUTCDATETIME())", s);
    }

    private const string SearchFilter = "(@pattern IS NULL OR SearchText LIKE @pattern ESCAPE '\\')";

    private static string? Pattern(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        var escaped = query.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
        return "%" + escaped + "%";
    }

    public async Task<IReadOnlyList<FormSubmission>> ListSubmissionsAsync(Guid formId, Guid ownerId, string? query, int skip, int take)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<FormSubmission>($@"
            SELECT * FROM FormSubmissions
            WHERE FormId = @formId AND OwnerId = @ownerId AND {SearchFilter}
            ORDER BY CreatedAt DESC, Id DESC
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY", new { formId, ownerId, pattern = Pattern(query), skip, take })).ToList();
    }

    public async Task<int> CountSubmissionsAsync(Guid formId, Guid ownerId, string? query)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteScalarAsync<int>($@"
            SELECT COUNT(*) FROM FormSubmissions WHERE FormId = @formId AND OwnerId = @ownerId AND {SearchFilter}",
            new { formId, ownerId, pattern = Pattern(query) });
    }

    public async Task<IReadOnlyList<FormSubmission>> AllSubmissionsAsync(Guid formId, Guid ownerId)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<FormSubmission>(
            "SELECT * FROM FormSubmissions WHERE FormId = @formId AND OwnerId = @ownerId ORDER BY CreatedAt, Id", new { formId, ownerId })).ToList();
    }

    public async Task<FormSubmission?> GetSubmissionAsync(long id, Guid ownerId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<FormSubmission>(
            "SELECT * FROM FormSubmissions WHERE Id = @id AND OwnerId = @ownerId", new { id, ownerId });
    }

    public async Task MarkReadAsync(long id, Guid ownerId, DateTime nowUtc)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync("UPDATE FormSubmissions SET ReadAt = @nowUtc WHERE Id = @id AND OwnerId = @ownerId AND ReadAt IS NULL",
            new { id, ownerId, nowUtc });
    }

    public async Task<int> DeleteSubmissionsAsync(Guid formId, Guid ownerId, IReadOnlyCollection<long> ids)
    {
        if (ids.Count == 0) return 0;
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteAsync("DELETE FROM FormSubmissions WHERE FormId = @formId AND OwnerId = @ownerId AND Id IN @ids",
            new { formId, ownerId, ids });
    }

    public async Task<bool> HasRecentRepeatAsync(Guid formId, string contentHash, DateTime sinceUtc)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM FormSubmissions WHERE FormId = @formId AND ContentHash = @contentHash AND CreatedAt >= @sinceUtc",
            new { formId, contentHash, sinceUtc }) > 0;
    }

    public async Task<int> CountRecentFromAddressAsync(Guid ownerId, string addressHash, DateTime sinceUtc)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM FormSubmissions WHERE OwnerId = @ownerId AND AddressHash = @addressHash AND CreatedAt >= @sinceUtc",
            new { ownerId, addressHash, sinceUtc });
    }

    public async Task<int> DeleteExpiredAsync(DateTime nowUtc)
    {
        using var conn = _ctx.CreateConnection();
        // Batches, so a first run on a long-neglected site never holds one large lock; the job runs daily, so what is
        // left over goes next time.
        var total = 0;
        for (var batch = 0; batch < 50; batch++)
        {
            var n = await conn.ExecuteAsync(@"
                DELETE TOP (2000) s FROM FormSubmissions s JOIN Forms f ON f.Id = s.FormId
                WHERE f.RetentionDays > 0 AND s.CreatedAt < DATEADD(DAY, -f.RetentionDays, @nowUtc)", new { nowUtc });
            total += n;
            if (n < 2000) break;
        }
        return total;
    }
}
