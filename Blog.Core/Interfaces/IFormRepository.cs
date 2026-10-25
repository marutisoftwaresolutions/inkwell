using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

/// <summary>Contact forms and their submissions (ADR-0030). Every method is scoped to the owner.</summary>
public interface IFormRepository
{
    Task<IReadOnlyList<ContactForm>> ListAsync(Guid ownerId);
    Task<ContactForm?> GetAsync(Guid id, Guid ownerId);
    Task<ContactForm?> GetByKeyAsync(string formKey, Guid ownerId);
    Task<bool> KeyExistsAsync(string formKey, Guid ownerId, Guid? exceptId);
    Task CreateAsync(ContactForm form);
    Task<bool> UpdateAsync(ContactForm form);
    Task<bool> DeleteAsync(Guid id, Guid ownerId);

    Task<long> AddSubmissionAsync(FormSubmission submission);
    /// <summary>Submissions of one form, newest first, SQL-paged, optionally searched.</summary>
    Task<IReadOnlyList<FormSubmission>> ListSubmissionsAsync(Guid formId, Guid ownerId, string? query, int skip, int take);
    Task<int> CountSubmissionsAsync(Guid formId, Guid ownerId, string? query);
    Task<IReadOnlyList<FormSubmission>> AllSubmissionsAsync(Guid formId, Guid ownerId);
    Task<FormSubmission?> GetSubmissionAsync(long id, Guid ownerId);
    Task MarkReadAsync(long id, Guid ownerId, DateTime nowUtc);
    Task<int> DeleteSubmissionsAsync(Guid formId, Guid ownerId, IReadOnlyCollection<long> ids);

    Task<bool> HasRecentRepeatAsync(Guid formId, string contentHash, DateTime sinceUtc);
    Task<int> CountRecentFromAddressAsync(Guid ownerId, string addressHash, DateTime sinceUtc);
    /// <summary>Deletes submissions older than each form's retention (forms with 0 keep theirs). Returns how many.</summary>
    Task<int> DeleteExpiredAsync(DateTime nowUtc);
}
