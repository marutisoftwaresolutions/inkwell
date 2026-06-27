namespace Blog.Core.Interfaces;

public interface IRedirectRepository
{
    Task UpsertAsync(string from, string to);
    Task<string?> GetDestinationAsync(string from);
}
