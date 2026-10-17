using System.ComponentModel.DataAnnotations;

namespace DocuMind.Api.Contracts;

public record RegisterRequest(
    [Required, StringLength(100, MinimumLength = 2)] string CompanyName,
    [Required, StringLength(100, MinimumLength = 1)] string DisplayName,
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MaxLength(200)] string Password);

public record LoginRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MaxLength(200)] string Password);

public record RefreshRequest([Required] string RefreshToken);

public record AcceptInvitationRequest(
    [Required, MaxLength(200)] string Token,
    [Required, StringLength(100, MinimumLength = 1)] string DisplayName,
    [Required, MaxLength(200)] string Password);

public record InvitationPreview(string CompanyName, string Email, string Role, DateTime ExpiresAt);

public record CurrentUserResponse(
    Guid UserId,
    string Email,
    string DisplayName,
    Guid TenantId,
    string TenantName,
    string Role);

public record TeamMember(Guid UserId, string Email, string DisplayName, string Role, DateTime JoinedAt);

public record PendingInvitation(Guid Id, string Email, string Role, DateTime CreatedAt, DateTime ExpiresAt);

public record CreateInvitationRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, RegularExpression("^(Admin|Member)$")] string Role);

// The token is only returned once, at creation; it can't be recovered later
public record CreatedInvitation(PendingInvitation Invitation, string Token);

public record ChangeRoleRequest([Required, RegularExpression("^(Admin|Member)$")] string Role);
