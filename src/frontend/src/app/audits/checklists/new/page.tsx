import type { Metadata } from "next";
import Link from "next/link";

import { ChecklistEditor } from "../checklist-editor";
import { requireAdmin } from "@/lib/auth";

export const metadata: Metadata = { title: "New checklist · DocuMind AI" };

export default async function NewChecklistPage() {
  await requireAdmin();
  return (
    <main className="mx-auto flex w-full max-w-4xl flex-col gap-6 px-4 py-8 sm:px-6">
      <div>
        <Link href="/audits" className="text-sm text-zinc-500 hover:text-zinc-900 dark:hover:text-zinc-100">
          ← All audits
        </Link>
        <h1 className="mt-2 text-xl font-semibold text-zinc-900 dark:text-zinc-100">New checklist</h1>
      </div>
      <ChecklistEditor checklist={null} />
    </main>
  );
}
