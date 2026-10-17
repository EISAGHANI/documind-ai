namespace DocuMind.Api.Entities;

public enum AuditSeverity
{
    Low,
    Medium,
    High
}

// A company's reusable list of requirements to check documents against
public class AuditChecklist
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<AuditCheck> Checks { get; set; } = [];
}

public class AuditCheck
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChecklistId { get; set; }
    public AuditChecklist Checklist { get; set; } = null!;
    public int Position { get; set; }

    // Short name shown in reports, e.g. "Limitation of liability"
    public string Title { get; set; } = string.Empty;

    // What the document must say, in plain language; this is what the model evaluates
    public string Requirement { get; set; } = string.Empty;
    public AuditSeverity Severity { get; set; } = AuditSeverity.Medium;
}

public enum AuditRunStatus
{
    Queued,
    Running,
    Completed,
    Failed
}

// One audit of one document against one checklist. The checks are copied into the run when it
// starts, so editing or deleting the checklist later doesn't change existing reports.
public class AuditRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public Guid? ChecklistId { get; set; }
    public string ChecklistName { get; set; } = string.Empty;
    public List<AuditRunCheck> Checks { get; set; } = [];
    public AuditRunStatus Status { get; set; } = AuditRunStatus.Queued;
    public string? Error { get; set; }

    // Which provider and model produced the findings, for the report
    public string? Provider { get; set; }
    public string? Model { get; set; }

    // True when only the passages most relevant to each check were reviewed (large documents)
    public bool UsedRetrieval { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<AuditFinding> Findings { get; set; } = [];
}

// Snapshot of a check at the time the run was created (stored as JSON on the run)
public class AuditRunCheck
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Requirement { get; set; } = string.Empty;
    public AuditSeverity Severity { get; set; }
}

public enum AuditResult
{
    // The document clearly meets the requirement
    Pass,

    // The document addresses the topic but falls short of, or contradicts, the requirement
    Fail,

    // Nothing in the reviewed text addresses the requirement
    NotFound,

    // The requirement doesn't apply to this kind of document
    NotApplicable
}

public class AuditFinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public AuditRun Run { get; set; } = null!;
    public int Position { get; set; }
    public string CheckTitle { get; set; } = string.Empty;
    public string Requirement { get; set; } = string.Empty;
    public AuditSeverity Severity { get; set; }
    public AuditResult Result { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public List<AuditEvidence> Evidence { get; set; } = [];
}

// A quote supporting a finding (stored as JSON on the finding)
public class AuditEvidence
{
    public string Quote { get; set; } = string.Empty;
    public int? PageNumber { get; set; }

    // Whether the quote was found word-for-word in the document; unverified quotes are flagged in the UI
    public bool Verified { get; set; }
}
