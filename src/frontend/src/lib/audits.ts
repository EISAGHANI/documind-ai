// Shared types for compliance audits: checklists, runs and their findings

export type AuditSeverity = "Low" | "Medium" | "High";
export type AuditResult = "Pass" | "Fail" | "NotFound" | "NotApplicable";
export type AuditRunStatus = "Queued" | "Running" | "Completed" | "Failed";

export type Check = {
  id: string;
  title: string;
  requirement: string;
  severity: AuditSeverity;
};

export type Checklist = {
  id: string;
  name: string;
  description: string | null;
  updatedAt: string;
  checks: Check[];
};

export type ChecklistTemplate = {
  key: string;
  name: string;
  description: string;
  checkCount: number;
};

export type CheckInput = Omit<Check, "id">;

export type ChecklistInput = {
  name: string;
  description: string | null;
  checks: CheckInput[];
};

export type AuditCounts = {
  pass: number;
  fail: number;
  notFound: number;
  notApplicable: number;
};

export type AuditRunSummary = {
  id: string;
  documentId: string;
  documentTitle: string;
  checklistName: string;
  status: AuditRunStatus;
  error: string | null;
  checkCount: number;
  counts: AuditCounts;
  createdAt: string;
  completedAt: string | null;
};

export type Evidence = {
  quote: string;
  pageNumber: number | null;
  // Whether the quote was found word-for-word in the document
  verified: boolean;
};

export type Finding = {
  checkTitle: string;
  requirement: string;
  severity: AuditSeverity;
  result: AuditResult;
  explanation: string;
  evidence: Evidence[];
};

export type AuditRunDetail = {
  summary: AuditRunSummary;
  provider: string | null;
  model: string | null;
  usedRetrieval: boolean;
  findings: Finding[];
};

export const SEVERITIES: readonly AuditSeverity[] = ["High", "Medium", "Low"];

// Must match the API's limits (AuditOptions.MaxChecksPerChecklist and the contract lengths)
export const MAX_CHECKS_PER_CHECKLIST = 40;
export const MAX_CHECKLIST_NAME = 100;
export const MAX_CHECKLIST_DESCRIPTION = 500;
export const MAX_CHECK_TITLE = 200;
export const MAX_CHECK_REQUIREMENT = 1000;

export const RESULT_LABELS: Record<AuditResult, string> = {
  Pass: "Pass",
  Fail: "Fail",
  NotFound: "Not found",
  NotApplicable: "Not applicable",
};

export const isRunActive = (status: AuditRunStatus) => status === "Queued" || status === "Running";
