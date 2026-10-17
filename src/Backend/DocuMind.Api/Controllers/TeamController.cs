using DocuMind.Api.Contracts;
using DocuMind.Api.Data;
using DocuMind.Api.Entities;
using DocuMind.Api.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DocuMind.Api.Controllers;

// Members of the current company and their invitations. Changes are Admin-only.
// All queries go through the tenant query filter, so another company's people are never visible.
[ApiController]
[Route("api/team")]
public class TeamController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    ITenantProvider tenantProvider,
    IMemoryCache cache) : ControllerBase
{
    [HttpGet("members")]
    public async Task<ActionResult<IEnumerable<TeamMember>>> GetMembers(CancellationToken cancellationToken) =>
        await db.TenantMemberships
            .AsNoTracking()
            .OrderBy(m => m.JoinedAt)
            .Select(m => new TeamMember(m.UserId, m.User.Email ?? "", m.User.DisplayName, m.Role.ToString(), m.JoinedAt))
            .ToListAsync(cancellationToken);

    [Authorize(Policy = TenantClaims.AdminPolicy)]
    [HttpGet("invitations")]
    public async Task<ActionResult<IEnumerable<PendingInvitation>>> GetInvitations(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return await db.TenantInvitations
            .AsNoTracking()
            .Where(i => i.AcceptedAt == null && i.ExpiresAt > now)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new PendingInvitation(i.Id, i.Email, i.Role.ToString(), i.CreatedAt, i.ExpiresAt))
            .ToListAsync(cancellationToken);
    }

    // Returns the invitation token once; the frontend turns it into a link the admin shares
    [Authorize(Policy = TenantClaims.AdminPolicy)]
    [HttpPost("invitations")]
    public async Task<ActionResult<CreatedInvitation>> CreateInvitation(
        CreateInvitationRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return Problem("Someone with this email already has a DocuMind account.", statusCode: StatusCodes.Status409Conflict);
        }

        // Re-inviting replaces any earlier pending invitation for the same email
        var earlier = await db.TenantInvitations
            .Where(i => i.Email == email && i.AcceptedAt == null)
            .ToListAsync(cancellationToken);
        db.TenantInvitations.RemoveRange(earlier);

        var token = InvitationTokens.Create();
        var invitation = new TenantInvitation
        {
            TenantId = tenantProvider.TenantId,
            Email = email,
            Role = Enum.Parse<TenantRole>(request.Role),
            TokenHash = InvitationTokens.Hash(token),
            InvitedByUserId = Guid.Parse(userManager.GetUserId(User)!),
            ExpiresAt = DateTime.UtcNow.Add(InvitationTokens.Lifetime),
        };
        db.TenantInvitations.Add(invitation);
        await db.SaveChangesAsync(cancellationToken);

        return new CreatedInvitation(
            new PendingInvitation(invitation.Id, invitation.Email, invitation.Role.ToString(), invitation.CreatedAt, invitation.ExpiresAt),
            token);
    }

    [Authorize(Policy = TenantClaims.AdminPolicy)]
    [HttpDelete("invitations/{id:guid}")]
    public async Task<IActionResult> RevokeInvitation(Guid id, CancellationToken cancellationToken)
    {
        await db.TenantInvitations.Where(i => i.Id == id).ExecuteDeleteAsync(cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = TenantClaims.AdminPolicy)]
    [HttpPut("members/{userId:guid}/role")]
    public async Task<IActionResult> ChangeRole(Guid userId, ChangeRoleRequest request, CancellationToken cancellationToken)
    {
        var membership = await db.TenantMemberships.FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (membership is null)
        {
            return NotFound();
        }

        var role = Enum.Parse<TenantRole>(request.Role);
        if (membership.Role == TenantRole.Admin && role != TenantRole.Admin && await IsLastAdminAsync(cancellationToken))
        {
            return Problem("A company needs at least one admin.", statusCode: StatusCodes.Status409Conflict);
        }

        membership.Role = role;
        await db.SaveChangesAsync(cancellationToken);
        cache.Remove(TenantClaims.MembershipCacheKey(userId, tenantProvider.TenantId));
        return NoContent();
    }

    // Removes the person from the company and deletes their account (accounts belong to one company)
    [Authorize(Policy = TenantClaims.AdminPolicy)]
    [HttpDelete("members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid userId, CancellationToken cancellationToken)
    {
        if (userManager.GetUserId(User) == userId.ToString())
        {
            return Problem("You can't remove yourself.", statusCode: StatusCodes.Status409Conflict);
        }

        var membership = await db.TenantMemberships.Include(m => m.User)
            .FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (membership is null)
        {
            return NotFound();
        }

        if (membership.Role == TenantRole.Admin && await IsLastAdminAsync(cancellationToken))
        {
            return Problem("A company needs at least one admin.", statusCode: StatusCodes.Status409Conflict);
        }

        // Deleting the user also deletes the membership (cascade) and invalidates their refresh token
        var deleted = await userManager.DeleteAsync(membership.User);
        cache.Remove(TenantClaims.MembershipCacheKey(userId, tenantProvider.TenantId));
        return deleted.Succeeded
            ? NoContent()
            : Problem(string.Join(" ", deleted.Errors.Select(e => e.Description)), statusCode: StatusCodes.Status400BadRequest);
    }

    private async Task<bool> IsLastAdminAsync(CancellationToken cancellationToken) =>
        await db.TenantMemberships.CountAsync(m => m.Role == TenantRole.Admin, cancellationToken) <= 1;
}
