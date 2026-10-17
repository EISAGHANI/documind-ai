import { Suspense } from "react";

import { DocumentList } from "./document-list";
import { UploadForm } from "./upload-form";

export default function Home() {
  return (
    <div className="flex flex-1 flex-col">
      <main className="mx-auto flex w-full max-w-4xl flex-col gap-6 px-4 py-8 sm:px-6">
        <section className="rounded-2xl border border-zinc-200 bg-white p-6 dark:border-zinc-800 dark:bg-zinc-900">
          <h1 className="text-xl font-semibold text-zinc-900 dark:text-zinc-100">Upload a document</h1>
          <p className="mb-5 mt-1 text-sm text-zinc-500">
            Contracts, compliance records and manuals are indexed for search once processed.
          </p>
          <UploadForm />
        </section>

        <section className="rounded-2xl border border-zinc-200 bg-white p-6 dark:border-zinc-800 dark:bg-zinc-900">
          <h2 className="text-lg font-semibold text-zinc-900 dark:text-zinc-100">Documents</h2>
          <Suspense fallback={<p className="py-8 text-center text-sm text-zinc-500">Loading documents…</p>}>
            <DocumentList />
          </Suspense>
        </section>
      </main>
    </div>
  );
}
