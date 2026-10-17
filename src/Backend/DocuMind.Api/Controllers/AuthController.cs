using DocuMind.Api.Contracts;
using DocuMind.Api.Data;
using DocuMind.Api.Entities;
using DocuMind.Api.Tenancy;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DocuMind.Api.Controllers;

// Sign-up, sign-in and token refresh. Successful calls return Identity's bearer token response:
// { tokenType, accessToken, expiresIn, refreshToken }.
[ApiController]
[Route("api/auth")]
public class AuthController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IOptionsMonitor<BearerTokenOptions> bearerOptions,
    TimeProvider timeProvider) : ControllerBase
{
    // Creates a company and its first user, who becomes its Admin
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return Problem("An account with this email already exists. Sign in instead.", statusCode: StatusCodes.Status409Conflict);
        }

        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = request.DisplayName.Trim() };
        var tenant = new Tenant { Name = request.CompanyName.Trim() };

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(cancellationToken);

            var created = await userManager.CreateAsync(user, request.Password);
            if (!created.Succeeded)
            {
                return IdentityProblem(created);
            }

            db.TenantMemberships.Add(new TenantMembership { TenantId = tenant.Id, UserId = user.Id, Role = TenantRole.Admin });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await SignInAsync(user);
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;
        var result = await signInManager.PasswordSignInAsync(
            request.Email.Trim(), request.Password, isPersistent: false, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            return Problem("Too many failed attempts. Try again in a few minutes.", statusCode: StatusCodes.Status429TooManyRequests);
        }

        // The bearer handler has already written the token response
        return result.Succeeded
            ? new EmptyResult()
            : Problem("Incorrect email or password.", statusCode: StatusCodes.Status401Unauthorized);
    }

    // Exchanges a refresh token for new tokens with up-to-date claims (company, role)
    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest request)
    {
        var protector = bearerOptions.Get(IdentityConstants.BearerScheme).RefreshTokenProtector;
        var ticket = protector.Unprotect(request.RefreshToken);

        if (ticket?.Properties.ExpiresUtc is not { } expiresUtc
            || timeProvider.GetUtcNow() >= expiresUtc
            || await signInManager.ValidateSecurityStampAsync(ticket.Principal) is not { } user)
        {
            return Problem("Your session has expired. Please sign in again.", statusCode: StatusCodes.Status401Unauthorized);
        }

        return await SignInAsync(user);
    }

    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserResponse>> Me(CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (!Guid.TryParse(userId, out var id))
        {
            return Unauthorized();
        }

        var membership = await db.TenantMemberships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(m => m.Tenant)
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.UserId == id, cancellationToken);

        // The account may have been removed from its company since the token was issued
        if (membership is null)
        {
            return Unauthorized();
        }

        return new CurrentUserResponse(
            membership.UserId,
            membership.User.Email ?? string.Empty,
            membership.User.DisplayName,
            membership.TenantId,
            membership.Tenant.Name,
            membership.Role.ToString());
    }

    [AllowAnonymous]
    [HttpGet("invitations/{token}")]
    public async Task<ActionResult<InvitationPreview>> GetInvitation(string token, CancellationToken cancellationToken)
    {
        var invitation = await FindOpenInvitationAsync(token, cancellationToken);
        return invitation is null
            ? Problem("This invitation link is invalid or has expired.", statusCode: StatusCodes.Status404NotFound)
            : new InvitationPreview(invitation.Tenant.Name, invitation.Email, invitation.Role.ToString(), invitation.ExpiresAt);
    }

    // Creates the invited person's account and adds them to the company
    [AllowAnonymous]
    [HttpPost("invitations/accept")]
    public async Task<IActionResult> AcceptInvitation(AcceptInvitationRequest request, CancellationToken cancellationToken)
    {
        var invitation = await FindOpenInvitationAsync(request.Token, cancellationToken);
        if (invitation is null)
        {
            return Problem("This invitation link is invalid or has expired.", statusCode: StatusCodes.Status404NotFound);
        }

        if (await userManager.FindByEmailAsync(invitation.Email) is not null)
        {
            return Problem("An account with this email already exists. Accounts can belong to one company for now.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var user = new ApplicationUser
        {
            UserName = invitation.Email,
            Email = invitation.Email,
            // The invitation link went to this address, which confirms it
            EmailConfirmed = true,
            DisplayName = request.DisplayName.Trim(),
        };

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var created = await userManager.CreateAsync(user, request.Password);
            if (!created.Succeeded)
            {
                return IdentityProblem(created);
            }

            db.TenantMemberships.Add(new TenantMembership { TenantId = invitation.TenantId, UserId = user.Id, Role = invitation.Role });
            invitation.AcceptedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await SignInAsync(user);
    }

    private Task<TenantInvitation?> FindOpenInvitationAsync(string token, CancellationToken cancellationToken)
    {
        var hash = InvitationTokens.Hash(token);
        var now = DateTime.UtcNow;
        return db.TenantInvitations
            .IgnoreQueryFilters()
            .Include(i => i.Tenant)
            .FirstOrDefaultAsync(i => i.TokenHash == hash && i.AcceptedAt == null && i.ExpiresAt > now, cancellationToken);
    }

    // Issues tokens; the bearer handler writes them to the response
    private async Task<IActionResult> SignInAsync(ApplicationUser user)
    {
        var principal = await signInManager.CreateUserPrincipalAsync(user);
        return SignIn(principal, IdentityConstants.BearerScheme);
    }

    private ObjectResult IdentityProblem(IdentityResult result) =>
        Problem(string.Join(" ", result.Errors.Select(e => e.Description)), statusCode: StatusCodes.Status400BadRequest);
}
