import { DeleteButton } from "./delete-button";
import { listDocuments } from "@/lib/api";
import { formatBytes, type DocumentStatus, type DocumentSummary } from "@/lib/documents";

const statusStyles: Record<DocumentStatus, string> = {
  Pending: "bg-amber-100 text-amber-800 dark:bg-amber-950/60 dark:text-amber-300",
  Processing: "bg-sky-100 text-sky-800 dark:bg-sky-950/60 dark:text-sky-300",
  Processed: "bg-emerald-100 text-emerald-800 dark:bg-emerald-950/60 dark:text-emerald-300",
  Failed: "bg-red-100 text-red-800 dark:bg-red-950/60 dark:text-red-300",
};

const dateFormat = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" });

export async function DocumentList() {
  let documents: DocumentSummary[];
  try {
    documents = await listDocuments();
  } catch {
    return (
      <p className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950/40 dark:text-red-300">
        Could not load documents. Make sure the DocuMind API and database are running.
      </p>
    );
  }

  if (documents.length === 0) {
    return <p className="py-8 text-center text-sm text-zinc-500">No documents yet. Upload your first PDF above.</p>;
  }

  return (
    <ul className="divide-y divide-zinc-200 dark:divide-zinc-800">
      {documents.map((document) => (
        <li key={document.id} className="flex flex-col gap-2 py-4 sm:flex-row sm:items-center sm:justify-between">
          <div className="min-w-0">
            <a
              href={`/documents/${document.id}/file`}
              className="block truncate font-medium text-zinc-900 hover:text-indigo-600 dark:text-zinc-100 dark:hover:text-indigo-400"
            >
              {document.title}
            </a>
            <p className="truncate text-sm text-zinc-500">
              {document.fileName} · {formatBytes(document.fileSizeBytes)} ·{" "}
              {dateFormat.format(new Date(document.uploadedAt))}
            </p>
          </div>
          <div className="flex shrink-0 items-center gap-3">
            <span className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${statusStyles[document.status]}`}>
              {document.status}
            </span>
            <DeleteButton id={document.id} title={document.title} />
          </div>
        </li>
      ))}
    </ul>
  );
}
