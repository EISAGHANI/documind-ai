import type { AuditResult, AuditRunStatus, AuditSeverity } from "@/lib/audits";

export const badge = "rounded-full px-2.5 py-0.5 text-xs font-medium";

export const runStatusStyles: Record<AuditRunStatus, string> = {
  Queued: "bg-amber-100 text-amber-800 dark:bg-amber-950/60 dark:text-amber-300",
  Running: "bg-sky-100 text-sky-800 dark:bg-sky-950/60 dark:text-sky-300",
  Completed: "bg-emerald-100 text-emerald-800 dark:bg-emerald-950/60 dark:text-emerald-300",
  Failed: "bg-red-100 text-red-800 dark:bg-red-950/60 dark:text-red-300",
};

export const resultStyles: Record<AuditResult, string> = {
  Pass: "bg-emerald-100 text-emerald-800 dark:bg-emerald-950/60 dark:text-emerald-300",
  Fail: "bg-red-100 text-red-800 dark:bg-red-950/60 dark:text-red-300",
  NotFound: "bg-amber-100 text-amber-800 dark:bg-amber-950/60 dark:text-amber-300",
  NotApplicable: "bg-zinc-100 text-zinc-700 dark:bg-zinc-800 dark:text-zinc-300",
};

export const severityStyles: Record<AuditSeverity, string> = {
  High: "text-red-700 dark:text-red-400",
  Medium: "text-amber-700 dark:text-amber-400",
  Low: "text-zinc-500",
};

export const inputClass =
  "rounded-lg border border-zinc-300 bg-white px-3 py-2 text-sm text-zinc-900 outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500/20 dark:border-zinc-700 dark:bg-zinc-950 dark:text-zinc-100";

export const primaryButton =
  "rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-500 disabled:opacity-50";

export const smallButton =
  "rounded-md px-2 py-1 text-sm transition-colors disabled:opacity-50 hover:bg-zinc-100 dark:hover:bg-zinc-800";
