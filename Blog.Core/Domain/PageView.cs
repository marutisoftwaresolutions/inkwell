namespace Blog.Core.Domain;

public class PageView
{
    public long Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Path { get; set; } = string.Empty;
    public string? Referrer { get; set; }
    public string? Source { get; set; }
    public string? Medium { get; set; }
    public string? Campaign { get; set; }
    public string? Term { get; set; }
    public string? Content { get; set; }
    public string? IpHash { get; set; }
    public string? UserAgent { get; set; }
    public string? Country { get; set; }
    public string? Region { get; set; }
    public DateTime CreatedAt { get; set; }
}
