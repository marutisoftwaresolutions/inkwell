namespace Blog.Core.Domain;

public class User
{
    public Guid Id { get; set; }
    public Guid Uuid { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "Author";
    public string? Status { get; set; } = "active";
    public Guid? CreatedByUserId { get; set; }
    
    public string? Bio { get; set; }
    public string? ProfileImage { get; set; } // replacing AvatarUrl logically but retaining mapping
    public string? AvatarUrl { get; set; } // keep for backward compatibility during migration
    public string? CoverImage { get; set; }
    public string? Website { get; set; }
    public string? Twitter { get; set; }
    public string? Facebook { get; set; }

    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }

    // E-E-A-T: professional credentials displayed on post bylines and in author schema
    public string? Credentials { get; set; }   // e.g. "OD, FAAO"
    public string? Specialty { get; set; }     // e.g. "Pediatric Optometry"
    public string? LicenseNumber { get; set; } // e.g. "CA-OD-12345"

    public bool IsActive { get; set; } = true;
    public DateTime? LastLogin { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
