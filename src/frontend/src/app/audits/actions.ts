"use server";

import { refresh } from "next/cache";
import { redirect } from "next/navigation";

import {
  ApiError,
  createChecklist,
  createChecklistFromTemplate,
  deleteAuditRun,
  deleteChecklist,
  startAuditRun,
  updateChecklist,
} from "@/lib/api";
import {
  MAX_CHECK_REQUIREMENT,
  MAX_CHECK_TITLE,
  MAX_CHECKLIST_DESCRIPTION,
  MAX_CHECKLIST_NAME,
  MAX_CHECKS_PER_CHECKLIST,
  SEVERITIES,
  type ChecklistInput,
} from "@/lib/audits";

export type AuditActionResult = { ok: true } | { ok: false; error: string };

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const isGuid = (value: unknown): value is string => typeof value === "string" && GUID_PATTERN.test(value);

function fail(error: unknown): { ok: false; error: string } {
  return {
    ok: false,
    error: error instanceof ApiError ? error.message : "Could not reach the DocuMind API. Is the backend running?",
  };
}

// Server Actions are public endpoints: rebuild the checklist from validated fields only
function parseChecklist(input: ChecklistInput): ChecklistInput | string {
  const name = String(input?.name ?? "").trim();
  const description = String(input?.description ?? "").trim();
  if (!name || name.length > MAX_CHECKLIST_NAME) return `Give the checklist a name (up to ${MAX_CHECKLIST_NAME} characters).`;
  if (description.length > MAX_CHECKLIST_DESCRIPTION) return `Keep the description under ${MAX_CHECKLIST_DESCRIPTION} characters.`;
  if (!Array.isArray(input.checks) || input.checks.length === 0) return "Add at least one check.";
  if (input.checks.length > MAX_CHECKS_PER_CHECKLIST) return `A checklist can have at most ${MAX_CHECKS_PER_CHECKLIST} checks.`;

  const checks = [];
  for (const [index, check] of input.checks.entries()) {
    const title = String(check?.title ?? "").trim();
    const requirement = String(check?.requirement ?? "").trim();
    if (!title || title.length > MAX_CHECK_TITLE) return `Check ${index + 1} needs a title (up to ${MAX_CHECK_TITLE} characters).`;
    if (!requirement || requirement.length > MAX_CHECK_REQUIREMENT) {
      return `Check ${index + 1} needs a requirement (up to ${MAX_CHECK_REQUIREMENT} characters).`;
    }
    if (!SEVERITIES.includes(check.severity)) return `Choose a severity for check ${index + 1}.`;
    checks.push({ title, requirement, severity: check.severity });
  }

  return { name, description: description || null, checks };
}

export async function startAuditAction(documentId: string, checklistId: string): Promise<AuditActionResult> {
  if (!isGuid(documentId)) return { ok: false, error: "Choose a document." };
  if (!isGuid(checklistId)) return { ok: false, error: "Choose a checklist." };

  let runId: string;
  try {
    runId = (await startAuditRun(documentId, checklistId)).id;
  } catch (error) {
    return fail(error);
  }
  redirect(`/audits/runs/${runId}`);
}

export async function deleteAuditRunAction(id: string): Promise<AuditActionResult> {
  if (!isGuid(id)) return { ok: false, error: "Invalid report." };
  try {
    await deleteAuditRun(id);
    refresh();
    return { ok: true };
  } catch (error) {
    return fail(error);
  }
}

// Admin-only actions below; the API enforces the role

export async function createFromTemplateAction(key: string): Promise<AuditActionResult> {
  if (typeof key !== "string" || !/^[a-z0-9-]{1,50}$/.test(key)) return { ok: false, error: "Unknown template." };
  let checklistId: string;
  try {
    checklistId = (await createChecklistFromTemplate(key)).id;
  } catch (error) {
    return fail(error);
  }
  redirect(`/audits/checklists/${checklistId}`);
}

// Creates a checklist when id is null, otherwise replaces it
export async function saveChecklistAction(id: string | null, input: ChecklistInput): Promise<AuditActionResult> {
  if (id !== null && !isGuid(id)) return { ok: false, error: "Invalid checklist." };
  const checklist = parseChecklist(input);
  if (typeof checklist === "string") return { ok: false, error: checklist };

  try {
    if (id === null) {
      await createChecklist(checklist);
    } else {
      await updateChecklist(id, checklist);
    }
  } catch (error) {
    return fail(error);
  }
  redirect("/audits");
}

export async function deleteChecklistAction(id: string): Promise<AuditActionResult> {
  if (!isGuid(id)) return { ok: false, error: "Invalid checklist." };
  try {
    await deleteChecklist(id);
  } catch (error) {
    return fail(error);
  }
  redirect("/audits");
}
