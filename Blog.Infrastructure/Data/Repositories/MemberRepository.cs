using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class MemberRepository : IMemberRepository
{
    private readonly DapperContext _context;

    public MemberRepository(DapperContext context) => _context = context;

    public async Task<Member?> GetByEmailAsync(string email)
    {
        using var conn = _context.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Member>(
            "SELECT * FROM Members WHERE Email = @Email AND DeletedAt IS NULL", new { Email = email });
    }

    public async Task<Member?> GetByConfirmTokenAsync(string token)
    {
        using var conn = _context.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Member>(
            "SELECT * FROM Members WHERE ConfirmToken = @Token AND DeletedAt IS NULL", new { Token = token });
    }

    public async Task<Member?> GetByUnsubscribeTokenAsync(string token)
    {
        using var conn = _context.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Member>(
            "SELECT * FROM Members WHERE UnsubscribeToken = @Token AND DeletedAt IS NULL", new { Token = token });
    }

    public async Task<Guid> CreateAsync(Member member)
    {
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO Members (Id, Uuid, Email, Name, Note, Status, Subscribed, ConfirmToken, UnsubscribeToken, ConfirmedAt, CreatedAt)
            VALUES (@Id, @Uuid, @Email, @Name, @Note, @Status, @Subscribed, @ConfirmToken, @UnsubscribeToken, @ConfirmedAt, @CreatedAt)",
            member);
        return member.Id;
    }

    public async Task ConfirmAsync(Guid id)
    {
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE Members SET Status = 'confirmed', Subscribed = 1, ConfirmedAt = @Now WHERE Id = @Id",
            new { Id = id, Now = DateTime.UtcNow });
    }

    public async Task UnsubscribeAsync(Guid id)
    {
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE Members SET Status = 'unsubscribed', Subscribed = 0 WHERE Id = @Id",
            new { Id = id });
    }

    public async Task<List<Member>> GetConfirmedAsync(int page = 1, int pageSize = 100)
    {
        using var conn = _context.CreateConnection();
        var offset = (page - 1) * pageSize;
        var rows = await conn.QueryAsync<Member>(@"
            SELECT * FROM Members WHERE Status = 'confirmed' AND DeletedAt IS NULL
            ORDER BY ConfirmedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
            new { Offset = offset, PageSize = pageSize });
        return rows.ToList();
    }

    public async Task<int> GetConfirmedCountAsync()
    {
        using var conn = _context.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Members WHERE Status = 'confirmed' AND DeletedAt IS NULL");
    }

    public async Task<(List<Member> Items, int Total)> GetPagedAsync(int page, int pageSize, string? status = null)
    {
        using var conn = _context.CreateConnection();
        var where = status is null
            ? "DeletedAt IS NULL"
            : "Status = @Status AND DeletedAt IS NULL";
        var total = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM Members WHERE {where}", new { Status = status });
        var offset = (page - 1) * pageSize;
        var rows = await conn.QueryAsync<Member>($@"
            SELECT * FROM Members WHERE {where}
            ORDER BY CreatedAt DESC
            OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY",
            new { Status = status });
        return (rows.ToList(), total);
    }

    public async Task DeleteAsync(Guid id)
    {
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE Members SET DeletedAt = @Now WHERE Id = @Id",
            new { Id = id, Now = DateTime.UtcNow });
    }
}
