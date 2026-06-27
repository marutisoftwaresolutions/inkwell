using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface IAuditRepository
{
    Task LogAsync(AuditLog entry);

    Task<(IReadOnlyList<AuditLog> Items, int Total)> GetPagedAsync(
        Guid ownerId,
        int page,
        int pageSize,
        string? entityType = null,
        string? action = null,
        Guid? userId = null,
        DateTime? from = null,
        DateTime? to = null);
}
