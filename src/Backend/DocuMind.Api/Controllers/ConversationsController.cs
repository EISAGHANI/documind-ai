using System.Security.Claims;
using System.Text.Json;
using DocuMind.Api.Chat;
using DocuMind.Api.Data;
using DocuMind.Api.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocuMind.Api.Controllers;

// A user's saved chat threads. Conversations are private to the person who started them.
[ApiController]
[Route("api/conversations")]
public class ConversationsController(ApplicationDbContext db) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private Guid UserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ConversationSummary>>> List(CancellationToken cancellationToken)
    {
        var userId = UserId;
        return await db.ChatConversations
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.UpdatedAt)
            .Take(50)
            .Select(c => new ConversationSummary(c.Id, c.Title, c.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ConversationDetail>> Get(Guid id, CancellationToken cancellationToken)
    {
        var userId = UserId;
        var conversation = await db.ChatConversations
            .AsNoTracking()
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, cancellationToken);
        if (conversation is null)
        {
            return NotFound();
        }

        var messages = conversation.Messages
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ConversationMessage(
                m.Id, m.Role, m.Content,
                m.SourcesJson is null ? null : JsonSerializer.Deserialize<List<ChatSource>>(m.SourcesJson, Json),
                m.CreatedAt))
            .ToList();
        return new ConversationDetail(conversation.Id, conversation.Title, messages);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var userId = UserId;
        await db.ChatConversations.Where(c => c.Id == id && c.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        return NoContent();
    }
}

public record ActivityLogItem(Guid Id, Guid? UserId, string Action, string? EntityType, string? EntityId,
    string? Detail, string? IpAddress, DateTime CreatedAt);

// Read-only view of the audit trail for company admins
[ApiController]
[Route("api/activity")]
[Authorize(Policy = TenantClaims.AdminPolicy)]
public class ActivityController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ActivityLogItem>>> List(
        DateTime? before, [FromQuery] int limit, CancellationToken cancellationToken)
    {
        var take = limit is > 0 and <= 200 ? limit : 50;
        var query = db.ActivityLog.AsNoTracking();
        if (before is { } cursor)
        {
            query = query.Where(e => e.CreatedAt < cursor);
        }

        return await query
            .OrderByDescending(e => e.CreatedAt)
            .Take(take)
            .Select(e => new ActivityLogItem(e.Id, e.UserId, e.Action, e.EntityType, e.EntityId, e.Detail, e.IpAddress, e.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
