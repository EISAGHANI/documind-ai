using Microsoft.AspNetCore.Identity;

namespace DocuMind.Api.Entities;

// A person who can sign in. UserName and Email are both the email address.
public class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// A customer company. Every document, chunk and API key belongs to exactly one tenant.
public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum TenantRole
{
    // Manages API keys and the team
    Admin,

    // Uploads documents and asks questions
    Member
}

// Links a user to their company. A user belongs to one company for now (unique UserId);
// the table already allows several so multi-company accounts can be added later.
public class TenantMembership
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public TenantRole Role { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

// An invitation link for someone to join a company. Only a hash of the token is stored,
// so a database leak doesn't expose usable links.
public class TenantInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    // Lower-cased email the invitation is for; the account is created with this email
    public string Email { get; set; } = string.Empty;
    public TenantRole Role { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public Guid InvitedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
}
