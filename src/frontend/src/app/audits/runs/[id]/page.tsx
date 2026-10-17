import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";

import { AutoRefresh } from "../../../auto-refresh";
import { badge, resultStyles, runStatusStyles, severityStyles } from "../../styles";
import { getAuditRun } from "@/lib/api";
import { isRunActive, RESULT_LABELS, type AuditResult, type Finding } from "@/lib/audits";
import { requireUser } from "@/lib/auth";

export const metadata: Metadata = { title: "Audit report · DocuMind AI" };

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const dateFormat = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" });

// Failures first so problems are seen before passes
const resultOrder: Record<AuditResult, number> = { Fail: 0, NotFound: 1, Pass: 2, NotApplicable: 3 };
const severityOrder = { High: 0, Medium: 1, Low: 2 };

const sortFindings = (findings: Finding[]) =>
  [...findings].sort(
    (a, b) => resultOrder[a.result] - resultOrder[b.result] || severityOrder[a.severity] - severityOrder[b.severity],
  );

export default async function AuditRunPage(props: PageProps<"/audits/runs/[id]">) {
  await requireUser();
  const { id } = await props.params;
  if (!GUID_PATTERN.test(id)) notFound();

  const run = await getAuditRun(id);
  if (!run) notFound();
  const { summary } = run;
  const active = isRunActive(summary.status);

  const tiles: { result: AuditResult; count: number }[] = [
    { result: "Fail", count: summary.counts.fail },
    { result: "NotFound", count: summary.counts.notFound },
    { result: "Pass", count: summary.counts.pass },
    { result: "NotApplicable", count: summary.counts.notApplicable },
  ];

  return (
    <main className="mx-auto flex w-full max-w-4xl flex-col gap-6 px-4 py-8 sm:px-6">
      <AutoRefresh active={active} />
      <div>
        <Link href="/audits" className="text-sm text-zinc-500 hover:text-zinc-900 dark:hover:text-zinc-100">
          ← All audits
        </Link>
        <div className="mt-2 flex flex-wrap items-center gap-3">
          <h1 className="min-w-0 break-words text-xl font-semibold text-zinc-900 dark:text-zinc-100">{summary.documentTitle}</h1>
          <span className={`${badge} ${runStatusStyles[summary.status]}`}>{summary.status}</span>
        </div>
        <p className="mt-1 text-sm text-zinc-500">
          {summary.checklistName} · {summary.checkCount} checks · started {dateFormat.format(new Date(summary.createdAt))}
          {run.model ? ` · reviewed by ${run.model}` : ""}
        </p>
      </div>

      {active ? (
        <p className="rounded-xl border border-sky-200 bg-sky-50 px-4 py-3 text-sm text-sky-900 dark:border-sky-900 dark:bg-sky-950/40 dark:text-sky-200">
          {summary.status === "Queued" ? "Waiting to start…" : "Reviewing the document. This can take a minute or two."} This page updates by
          itself.
        </p>
      ) : null}

      {summary.status === "Failed" ? (
        <p className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950/40 dark:text-red-300">
          {summary.error ?? "The audit could not be completed."}
        </p>
      ) : null}

      {summary.status === "Completed" ? (
        <>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            {tiles.map((tile) => (
              <div key={tile.result} className="rounded-2xl border border-zinc-200 bg-white p-4 dark:border-zinc-800 dark:bg-zinc-900">
                <div className="text-2xl font-semibold text-zinc-900 dark:text-zinc-100">{tile.count}</div>
                <div className="text-sm text-zinc-500">{RESULT_LABELS[tile.result]}</div>
              </div>
            ))}
          </div>

          <p className="text-sm text-zinc-500">
            AI-generated review: check the evidence before relying on a result. This is not legal advice.
            {run.usedRetrieval
              ? " This document is long, so only the passages most relevant to each check were reviewed; a \"Not found\" result may be worth checking by hand."
              : ""}
          </p>

          <ol className="flex flex-col gap-3">
            {sortFindings(run.findings).map((finding, index) => (
              <li key={index} className="rounded-2xl border border-zinc-200 bg-white p-5 dark:border-zinc-800 dark:bg-zinc-900">
                <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
                  <h2 className="font-semibold text-zinc-900 dark:text-zinc-100">{finding.checkTitle}</h2>
                  <span className={`${badge} ${resultStyles[finding.result]}`}>{RESULT_LABELS[finding.result]}</span>
                  <span className={`text-xs font-medium ${severityStyles[finding.severity]}`}>{finding.severity} severity</span>
                </div>
                <p className="mt-1 text-sm text-zinc-500">{finding.requirement}</p>
                <p className="mt-3 text-sm text-zinc-800 dark:text-zinc-200">{finding.explanation}</p>

                {finding.evidence.length > 0 ? (
                  <ul className="mt-3 flex flex-col gap-2">
                    {finding.evidence.map((evidence, i) => (
                      <li key={i} className="border-l-2 border-zinc-300 pl-3 text-sm dark:border-zinc-700">
                        <q className="italic text-zinc-700 dark:text-zinc-300">{evidence.quote}</q>
                        <div className="mt-1 flex flex-wrap gap-3 text-xs">
                          {evidence.pageNumber ? (
                            <a
                              href={`/documents/${summary.documentId}/file?inline=1#page=${evidence.pageNumber}`}
                              target="_blank"
                              rel="noreferrer"
                              className="font-medium text-indigo-600 hover:underline dark:text-indigo-400"
                            >
                              Page {evidence.pageNumber}
                            </a>
                          ) : null}
                          {!evidence.verified ? (
                            <span className="font-medium text-amber-700 dark:text-amber-400">
                              Couldn&apos;t match this quote to the document text; check it yourself
                            </span>
                          ) : null}
                        </div>
                      </li>
                    ))}
                  </ul>
                ) : null}
              </li>
            ))}
          </ol>
        </>
      ) : null}
    </main>
  );
}
