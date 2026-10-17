import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";

import { ChecklistEditor } from "../checklist-editor";
import { severityStyles } from "../../styles";
import { getChecklist } from "@/lib/api";
import { requireUser } from "@/lib/auth";

export const metadata: Metadata = { title: "Checklist · DocuMind AI" };

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// Admins edit the checklist; Members see it read-only
export default async function ChecklistPage(props: PageProps<"/audits/checklists/[id]">) {
  const user = await requireUser();
  const { id } = await props.params;
  if (!GUID_PATTERN.test(id)) notFound();

  const checklist = await getChecklist(id);
  if (!checklist) notFound();

  return (
    <main className="mx-auto flex w-full max-w-4xl flex-col gap-6 px-4 py-8 sm:px-6">
      <div>
        <Link href="/audits" className="text-sm text-zinc-500 hover:text-zinc-900 dark:hover:text-zinc-100">
          ← All audits
        </Link>
        <h1 className="mt-2 text-xl font-semibold text-zinc-900 dark:text-zinc-100">{checklist.name}</h1>
        {checklist.description ? <p className="mt-1 text-sm text-zinc-600 dark:text-zinc-400">{checklist.description}</p> : null}
        <p className="mt-1 text-sm text-zinc-500">
          Changes apply to new audits; existing reports keep the checks they were run with.
        </p>
      </div>

      {user.role === "Admin" ? (
        <ChecklistEditor checklist={checklist} />
      ) : (
        <ol className="flex flex-col gap-3">
          {checklist.checks.map((check) => (
            <li key={check.id} className="rounded-2xl border border-zinc-200 bg-white p-4 dark:border-zinc-800 dark:bg-zinc-900">
              <div className="flex flex-wrap items-center gap-3">
                <span className="font-medium text-zinc-900 dark:text-zinc-100">{check.title}</span>
                <span className={`text-xs font-medium ${severityStyles[check.severity]}`}>{check.severity} severity</span>
              </div>
              <p className="mt-1 text-sm text-zinc-600 dark:text-zinc-400">{check.requirement}</p>
            </li>
          ))}
        </ol>
      )}
    </main>
  );
}
