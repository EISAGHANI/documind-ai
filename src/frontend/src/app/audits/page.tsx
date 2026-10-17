import type { Metadata } from "next";
import Link from "next/link";

import { AutoRefresh } from "../auto-refresh";
import { SetupBanner } from "../setup-banner";
import { DeleteRunButton, StartAuditForm, TemplateButton } from "./audit-controls";
import { badge, runStatusStyles } from "./styles";
import { listAuditRuns, listChecklists, listChecklistTemplates, listDocuments } from "@/lib/api";
import type { AuditRunSummary, Checklist, ChecklistTemplate } from "@/lib/audits";
import { isRunActive } from "@/lib/audits";
import { requireUser } from "@/lib/auth";
import type { DocumentSummary } from "@/lib/documents";

export const metadata: Metadata = { title: "Audits · DocuMind AI" };

const dateFormat = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" });

const card = "rounded-2xl border border-zinc-200 bg-white p-6 dark:border-zinc-800 dark:bg-zinc-900";

export default async function AuditsPage() {
  const user = await requireUser();
  const isAdmin = user.role === "Admin";

  let documents: DocumentSummary[];
  let checklists: Checklist[];
  let runs: AuditRunSummary[];
  let templates: ChecklistTemplate[];
  try {
    [documents, checklists, runs, templates] = await Promise.all([
      listDocuments(),
      listChecklists(),
      listAuditRuns(),
      isAdmin ? listChecklistTemplates() : Promise.resolve([]),
    ]);
  } catch {
    return (
      <main className="mx-auto w-full max-w-4xl px-4 py-8 sm:px-6">
        <p className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950/40 dark:text-red-300">
          Could not load audits. Make sure the DocuMind API and database are running.
        </p>
      </main>
    );
  }

  const processed = documents.filter((d) => d.status === "Processed");

  return (
    <main className="mx-auto flex w-full max-w-4xl flex-col gap-6 px-4 py-8 sm:px-6">
      <SetupBanner needs="chat" />

      <section className={card}>
        <h1 className="text-xl font-semibold text-zinc-900 dark:text-zinc-100">Run a compliance audit</h1>
        <p className="mb-5 mt-1 text-sm text-zinc-500">
          Check a document against a checklist. Every result comes with quoted evidence you can verify.
        </p>
        {processed.length === 0 ? (
          <p className="text-sm text-zinc-500">
            No processed documents yet. <Link href="/" className="font-medium text-indigo-600 hover:underline dark:text-indigo-400">Upload one</Link> first.
          </p>
        ) : checklists.length === 0 ? (
          <p className="text-sm text-zinc-500">
            {isAdmin ? "Add a checklist below to get started." : "There are no checklists yet. Ask a company admin to create one."}
          </p>
        ) : (
          <StartAuditForm
            documents={processed.map((d) => ({ id: d.id, label: d.title }))}
            checklists={checklists.map((c) => ({ id: c.id, label: `${c.name} (${c.checks.length} checks)` }))}
          />
        )}
      </section>

      <section className={card}>
        <h2 className="text-lg font-semibold text-zinc-900 dark:text-zinc-100">Reports</h2>
        {runs.length === 0 ? (
          <p className="py-6 text-center text-sm text-zinc-500">No audits yet.</p>
        ) : (
          <ul className="mt-2 divide-y divide-zinc-200 dark:divide-zinc-800">
            <AutoRefresh active={runs.some((r) => isRunActive(r.status))} />
            {runs.map((run) => (
              <li key={run.id} className="flex flex-col gap-2 py-4 sm:flex-row sm:items-center sm:justify-between">
                <div className="min-w-0">
                  <Link
                    href={`/audits/runs/${run.id}`}
                    className="block truncate font-medium text-zinc-900 hover:text-indigo-600 dark:text-zinc-100 dark:hover:text-indigo-400"
                  >
                    {run.documentTitle}
                  </Link>
                  <p className="truncate text-sm text-zinc-500">
                    {run.checklistName} · {dateFormat.format(new Date(run.createdAt))}
                  </p>
                  {run.status === "Completed" ? (
                    <p className="mt-1 text-sm text-zinc-600 dark:text-zinc-400">
                      {run.counts.pass} pass · {run.counts.fail} fail · {run.counts.notFound} not found
                      {run.counts.notApplicable ? ` · ${run.counts.notApplicable} not applicable` : ""}
                    </p>
                  ) : null}
                  {run.status === "Failed" && run.error ? (
                    <p className="mt-1 text-sm text-red-600 dark:text-red-400">{run.error}</p>
                  ) : null}
                </div>
                <div className="flex shrink-0 items-center gap-3">
                  <span className={`${badge} ${runStatusStyles[run.status]}`}>{run.status}</span>
                  <DeleteRunButton id={run.id} label={run.documentTitle} />
                </div>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className={card}>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h2 className="text-lg font-semibold text-zinc-900 dark:text-zinc-100">Checklists</h2>
            <p className="mt-0.5 text-sm text-zinc-500">
              {isAdmin ? "Your company's requirements. Edit them to match your own policies." : "Admins manage checklists."}
            </p>
          </div>
          {isAdmin ? (
            <Link href="/audits/checklists/new" className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-500">
              New checklist
            </Link>
          ) : null}
        </div>

        {checklists.length > 0 ? (
          <ul className="mt-2 divide-y divide-zinc-200 dark:divide-zinc-800">
            {checklists.map((checklist) => (
              <li key={checklist.id} className="flex items-center justify-between gap-3 py-3">
                <div className="min-w-0">
                  <div className="truncate font-medium text-zinc-900 dark:text-zinc-100">{checklist.name}</div>
                  <div className="truncate text-sm text-zinc-500">
                    {checklist.checks.length} checks{checklist.description ? ` · ${checklist.description}` : ""}
                  </div>
                </div>
                <Link
                  href={`/audits/checklists/${checklist.id}`}
                  className="shrink-0 rounded-md px-2 py-1 text-sm text-indigo-600 hover:bg-indigo-50 dark:text-indigo-400 dark:hover:bg-indigo-950/40"
                >
                  {isAdmin ? "Edit" : "View"}
                </Link>
              </li>
            ))}
          </ul>
        ) : null}

        {isAdmin && templates.length > 0 ? (
          <div className="mt-5 border-t border-zinc-200 pt-4 dark:border-zinc-800">
            <h3 className="text-sm font-semibold text-zinc-900 dark:text-zinc-100">Start from a template</h3>
            <p className="text-sm text-zinc-500">Generic starting points, not legal advice. Review and adapt them before relying on results.</p>
            <ul className="mt-2 divide-y divide-zinc-200 dark:divide-zinc-800">
              {templates.map((template) => (
                <li key={template.key} className="flex items-center justify-between gap-3 py-3">
                  <div className="min-w-0">
                    <div className="font-medium text-zinc-900 dark:text-zinc-100">{template.name}</div>
                    <div className="text-sm text-zinc-500">
                      {template.checkCount} checks · {template.description}
                    </div>
                  </div>
                  <TemplateButton templateKey={template.key} />
                </li>
              ))}
            </ul>
          </div>
        ) : null}
      </section>
    </main>
  );
}
