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
    /// <summary>Puts someone who left, or whose address bounced, back to "pending" with a fresh confirmation token, and returns it.</summary>
    Task<string> RestartAsync(Guid id);
    /// <summary>Confirmed subscribers who have been sent exactly this many welcome emails and confirmed inside the window, oldest first.</summary>
    Task<List<Member>> GetWelcomeCandidatesAsync(int stepsSent, DateTime confirmedFromUtc, DateTime confirmedBeforeUtc, int take);
    Task SetWelcomeStepsSentAsync(Guid id, int stepsSent);
    Task MarkBouncedAsync(Guid id);
    /// <summary>The pages that brought the most confirmed subscribers, with how many each.</summary>
    Task<List<(string Path, int Subscribers)>> GetTopSignupPagesAsync(int take);
    Task<List<Member>> GetConfirmedAsync(int page = 1, int pageSize = 100);
    Task<int> GetConfirmedCountAsync();
    Task<(List<Member> Items, int Total)> GetPagedAsync(int page, int pageSize, string? status = null);
    Task DeleteAsync(Guid id);
}
