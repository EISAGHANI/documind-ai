using DocuMind.Api.Audits;
using DocuMind.Api.Contracts;
using DocuMind.Api.Data;
using DocuMind.Api.Entities;
using DocuMind.Api.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DocuMind.Api.Controllers;

// Checklists (Admins manage them) and audit runs (any member can start one).
// Everything is scoped to the signed-in user's company by the query filters.
[ApiController]
[Route("api/audits")]
public class AuditsController(
    ApplicationDbContext db,
    ITenantProvider tenantProvider,
    UserManager<ApplicationUser> userManager,
    IAuditQueue queue,
    IOptions<AuditOptions> options,
    DocuMind.Api.Activity.IActivityLog activity) : ControllerBase
{
    // --- Checklists

    [HttpGet("checklists")]
    public async Task<ActionResult<IEnumerable<ChecklistResponse>>> GetChecklists(CancellationToken cancellationToken)
    {
        var checklists = await db.AuditChecklists.AsNoTracking()
            .Include(c => c.Checks)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
        return Ok(checklists.Select(ToResponse));
    }

    [HttpGet("checklists/{id:guid}")]
    public async Task<ActionResult<ChecklistResponse>> GetChecklist(Guid id, CancellationToken cancellationToken)
    {
        var checklist = await db.AuditChecklists.AsNoTracking().Include(c => c.Checks)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return checklist is null ? NotFound() : ToResponse(checklist);
    }

    [HttpGet("templates")]
    public IEnumerable<ChecklistTemplateResponse> GetTemplates() =>
        ChecklistTemplates.All.Select(t => new ChecklistTemplateResponse(t.Key, t.Name, t.Description, t.Checks.Count));

    // Copies a starter template into the company's checklists so it can be edited
    [Authorize(Policy = TenantClaims.AdminPolicy)]
    [HttpPost("templates/{key}")]
    public async Task<ActionResult<ChecklistResponse>> CreateFromTemplate(string key, CancellationToken cancellationToken)
    {
        var template = ChecklistTemplates.Find(key);
        if (template is null)
        {
            return NotFound();
        }

        var checklist = new AuditChecklist
        {
            TenantId = tenantProvider.TenantId,
            Name = template.Name,
            Description = template.Description,
            Checks = template.Checks.Select((c, i) => new AuditCheck
            {
                Position = i, Title = c.Title, Requirement = c.Requirement, Severity = c.Severity,
            }).ToList(),
        };
        db.AuditChecklists.Add(checklist);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetChecklist), new { id = checklist.Id }, ToResponse(checklist));
    }

    [Authorize(Policy = TenantClaims.AdminPolicy)]
    [HttpPost("checklists")]
    public async Task<ActionResult<ChecklistResponse>> CreateChecklist(SaveChecklistRequest request, CancellationToken cancellationToken)
    {
        if (TooManyChecks(request))
        {
            return Problem($"A checklist can have at most {options.Value.MaxChecksPerChecklist} checks.", statusCode: StatusCodes.Status400BadRequest);
        }

        var checklist = new AuditChecklist { TenantId = tenantProvider.TenantId };
        Apply(checklist, request);
        db.AuditChecklists.Add(checklist);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetChecklist), new { id = checklist.Id }, ToResponse(checklist));
    }

    // Replaces the checklist's name, description and checks. Past audit reports are unaffected
    // because each run keeps its own copy of the checks.
    [Authorize(Policy = TenantClaims.AdminPolicy)]
    [HttpPut("checklists/{id:guid}")]
    public async Task<ActionResult<ChecklistResponse>> UpdateChecklist(
        Guid id, SaveChecklistRequest request, CancellationToken cancellationToken)
    {
        if (TooManyChecks(request))
        {
            return Problem($"A checklist can have at most {options.Value.MaxChecksPerChecklist} checks.", statusCode: StatusCodes.Status400BadRequest);
        }

        var checklist = await db.AuditChecklists.Include(c => c.Checks).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (checklist is null)
        {
            return NotFound();
        }

        db.AuditChecks.RemoveRange(checklist.Checks);
        Apply(checklist, request);
        // The new checks already have Ids, so EF would treat them as existing rows (and UPDATE
        // nothing) unless they are added explicitly
        db.AuditChecks.AddRange(checklist.Checks);
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(checklist);
    }

    [Authorize(Policy = TenantClaims.AdminPolicy)]
    [HttpDelete("checklists/{id:guid}")]
    public async Task<IActionResult> DeleteChecklist(Guid id, CancellationToken cancellationToken)
    {
        await db.AuditChecklists.Where(c => c.Id == id).ExecuteDeleteAsync(cancellationToken);
        return NoContent();
    }

    // --- Runs

    [HttpGet("runs")]
    public async Task<ActionResult<IEnumerable<AuditRunSummary>>> GetRuns(Guid? documentId, CancellationToken cancellationToken)
    {
        var query = db.AuditRuns.AsNoTracking();
        if (documentId is { } doc)
        {
            query = query.Where(r => r.DocumentId == doc);
        }

        var runs = await query.OrderByDescending(r => r.CreatedAt).Take(200)
            .Select(SummaryProjection)
            .ToListAsync(cancellationToken);
        return Ok(runs);
    }

    [HttpGet("runs/{id:guid}")]
    public async Task<ActionResult<AuditRunDetail>> GetRun(Guid id, CancellationToken cancellationToken)
    {
        var run = await db.AuditRuns.AsNoTracking()
            .Include(r => r.Document)
            .Include(r => r.Findings)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run is null)
        {
            return NotFound();
        }

        var summary = await db.AuditRuns.AsNoTracking().Where(r => r.Id == id).Select(SummaryProjection).FirstAsync(cancellationToken);
        return new AuditRunDetail(
            summary,
            run.Provider,
            run.Model,
            run.UsedRetrieval,
            run.Findings.OrderBy(f => f.Position).Select(f => new FindingResponse(
                f.CheckTitle,
                f.Requirement,
                f.Severity.ToString(),
                f.Result.ToString(),
                f.Explanation,
                f.Evidence.Select(e => new EvidenceResponse(e.Quote, e.PageNumber, e.Verified)).ToList())).ToList());
    }

    [HttpPost("runs")]
    [EnableRateLimiting(DocuMind.Api.Resilience.RateLimitPolicies.Expensive)]
    public async Task<ActionResult<AuditRunSummary>> StartRun(StartAuditRequest request, CancellationToken cancellationToken)
    {
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.DocumentId, cancellationToken);
        if (document is null)
        {
            return Problem("Document not found.", statusCode: StatusCodes.Status404NotFound);
        }
        if (document.Status != DocumentStatus.Processed)
        {
            return Problem("Only processed documents can be audited.", statusCode: StatusCodes.Status409Conflict);
        }

        var checklist = await db.AuditChecklists.AsNoTracking().Include(c => c.Checks)
            .FirstOrDefaultAsync(c => c.Id == request.ChecklistId, cancellationToken);
        if (checklist is null)
        {
            return Problem("Checklist not found.", statusCode: StatusCodes.Status404NotFound);
        }

        var run = new AuditRun
        {
            TenantId = tenantProvider.TenantId,
            DocumentId = document.Id,
            ChecklistId = checklist.Id,
            ChecklistName = checklist.Name,
            CreatedByUserId = Guid.Parse(userManager.GetUserId(User)!),
            // Snapshot the checks with short ids the model refers back to
            Checks = checklist.Checks.OrderBy(c => c.Position).Select((c, i) => new AuditRunCheck
            {
                Key = $"C{i + 1}", Title = c.Title, Requirement = c.Requirement, Severity = c.Severity,
            }).ToList(),
        };
        db.AuditRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        await queue.EnqueueAsync(run.Id, cancellationToken);
        await activity.RecordAsync("audit.run_start", "AuditRun", run.Id, checklist.Name, cancellationToken);

        var summary = await db.AuditRuns.AsNoTracking().Where(r => r.Id == run.Id).Select(SummaryProjection).FirstAsync(cancellationToken);
        return AcceptedAtAction(nameof(GetRun), new { id = run.Id }, summary);
    }

    [HttpDelete("runs/{id:guid}")]
    public async Task<IActionResult> DeleteRun(Guid id, CancellationToken cancellationToken)
    {
        await db.AuditRuns.Where(r => r.Id == id).ExecuteDeleteAsync(cancellationToken);
        return NoContent();
    }

    private static readonly System.Linq.Expressions.Expression<Func<AuditRun, AuditRunSummary>> SummaryProjection = r =>
        new AuditRunSummary(
            r.Id,
            r.DocumentId,
            r.Document.Title,
            r.ChecklistName,
            r.Status.ToString(),
            r.Error,
            r.Checks.Count,
            new AuditCounts(
                r.Findings.Count(f => f.Result == AuditResult.Pass),
                r.Findings.Count(f => f.Result == AuditResult.Fail),
                r.Findings.Count(f => f.Result == AuditResult.NotFound),
                r.Findings.Count(f => f.Result == AuditResult.NotApplicable)),
            r.CreatedAt,
            r.CompletedAt);

    private bool TooManyChecks(SaveChecklistRequest request) => request.Checks.Count > options.Value.MaxChecksPerChecklist;

    private static void Apply(AuditChecklist checklist, SaveChecklistRequest request)
    {
        checklist.Name = request.Name.Trim();
        checklist.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        checklist.UpdatedAt = DateTime.UtcNow;
        checklist.Checks = request.Checks.Select((c, i) => new AuditCheck
        {
            Position = i,
            Title = c.Title.Trim(),
            Requirement = c.Requirement.Trim(),
            Severity = Enum.Parse<AuditSeverity>(c.Severity),
        }).ToList();
    }

    private static ChecklistResponse ToResponse(AuditChecklist checklist) => new(
        checklist.Id,
        checklist.Name,
        checklist.Description,
        checklist.UpdatedAt,
        checklist.Checks.OrderBy(c => c.Position)
            .Select(c => new CheckResponse(c.Id, c.Title, c.Requirement, c.Severity.ToString()))
            .ToList());
}
