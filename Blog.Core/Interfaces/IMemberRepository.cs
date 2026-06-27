using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface IMemberRepository
{
    Task<Member?> GetByEmailAsync(string email);
    Task<Member?> GetByConfirmTokenAsync(string token);
    Task<Member?> GetByUnsubscribeTokenAsync(string token);
    Task<Guid> CreateAsync(Member member);
    Task ConfirmAsync(Guid id);
    Task UnsubscribeAsync(Guid id);
    Task<List<Member>> GetConfirmedAsync(int page = 1, int pageSize = 100);
    Task<int> GetConfirmedCountAsync();
    Task<(List<Member> Items, int Total)> GetPagedAsync(int page, int pageSize, string? status = null);
    Task DeleteAsync(Guid id);
}
