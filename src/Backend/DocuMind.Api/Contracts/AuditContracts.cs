using System.ComponentModel.DataAnnotations;

namespace DocuMind.Api.Contracts;

public record CheckInput(
    [Required, StringLength(200, MinimumLength = 1)] string Title,
    [Required, StringLength(1000, MinimumLength = 1)] string Requirement,
    [Required, RegularExpression("^(Low|Medium|High)$")] string Severity);

public record SaveChecklistRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [MaxLength(500)] string? Description,
    [Required, MinLength(1)] List<CheckInput> Checks);

public record CheckResponse(Guid Id, string Title, string Requirement, string Severity);

public record ChecklistResponse(
    Guid Id,
    string Name,
    string? Description,
    DateTime UpdatedAt,
    IReadOnlyList<CheckResponse> Checks);

public record ChecklistTemplateResponse(string Key, string Name, string Description, int CheckCount);

public record StartAuditRequest([Required] Guid DocumentId, [Required] Guid ChecklistId);

public record AuditCounts(int Pass, int Fail, int NotFound, int NotApplicable);

public record AuditRunSummary(
    Guid Id,
    Guid DocumentId,
    string DocumentTitle,
    string ChecklistName,
    string Status,
    string? Error,
    int CheckCount,
    AuditCounts Counts,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public record EvidenceResponse(string Quote, int? PageNumber, bool Verified);

public record FindingResponse(
    string CheckTitle,
    string Requirement,
    string Severity,
    string Result,
    string Explanation,
    IReadOnlyList<EvidenceResponse> Evidence);

public record AuditRunDetail(
    AuditRunSummary Summary,
    string? Provider,
    string? Model,
    bool UsedRetrieval,
    IReadOnlyList<FindingResponse> Findings);
