"use client";

import { useState, useTransition } from "react";

import { createFromTemplateAction, deleteAuditRunAction, startAuditAction } from "./actions";
import { inputClass, primaryButton, smallButton } from "./styles";

type Option = { id: string; label: string };

export function StartAuditForm({ documents, checklists }: { documents: Option[]; checklists: Option[] }) {
  const [documentId, setDocumentId] = useState(documents[0]?.id ?? "");
  const [checklistId, setChecklistId] = useState(checklists[0]?.id ?? "");
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();

  function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    startTransition(async () => {
      // Redirects to the report on success
      const result = await startAuditAction(documentId, checklistId);
      if (!result.ok) setError(result.error);
    });
  }

  return (
    <form onSubmit={submit} className="flex flex-col gap-3">
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="flex min-w-0 flex-col gap-1 text-sm font-medium text-zinc-700 dark:text-zinc-300">
          Document
          <select value={documentId} onChange={(event) => setDocumentId(event.target.value)} className={inputClass}>
            {documents.map((document) => (
              <option key={document.id} value={document.id}>
                {document.label}
              </option>
            ))}
          </select>
        </label>
        <label className="flex min-w-0 flex-col gap-1 text-sm font-medium text-zinc-700 dark:text-zinc-300">
          Checklist
          <select value={checklistId} onChange={(event) => setChecklistId(event.target.value)} className={inputClass}>
            {checklists.map((checklist) => (
              <option key={checklist.id} value={checklist.id}>
                {checklist.label}
              </option>
            ))}
          </select>
        </label>
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <button type="submit" disabled={pending || !documentId || !checklistId} className={primaryButton}>
          {pending ? "Starting…" : "Run audit"}
        </button>
        {error ? <p className="text-sm text-red-600 dark:text-red-400">{error}</p> : null}
      </div>
    </form>
  );
}

export function TemplateButton({ templateKey }: { templateKey: string }) {
  const [pending, startTransition] = useTransition();

  function handleClick() {
    startTransition(async () => {
      const result = await createFromTemplateAction(templateKey);
      if (!result.ok) window.alert(result.error);
    });
  }

  return (
    <button type="button" onClick={handleClick} disabled={pending} className={`${smallButton} shrink-0 text-indigo-600 dark:text-indigo-400`}>
      {pending ? "Adding…" : "Use template"}
    </button>
  );
}

export function DeleteRunButton({ id, label }: { id: string; label: string }) {
  const [pending, startTransition] = useTransition();

  function handleClick() {
    if (!window.confirm(`Delete the audit report for "${label}"? This cannot be undone.`)) return;
    startTransition(async () => {
      const result = await deleteAuditRunAction(id);
      if (!result.ok) window.alert(result.error);
    });
  }

  return (
    <button type="button" onClick={handleClick} disabled={pending} className={`${smallButton} text-red-600 dark:text-red-400`}>
      {pending ? "Deleting…" : "Delete"}
    </button>
  );
}
