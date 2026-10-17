"use client";

import { useState, useTransition } from "react";

import { deleteChecklistAction, saveChecklistAction } from "../actions";
import { inputClass, primaryButton, smallButton } from "../styles";
import {
  MAX_CHECK_REQUIREMENT,
  MAX_CHECK_TITLE,
  MAX_CHECKLIST_DESCRIPTION,
  MAX_CHECKLIST_NAME,
  MAX_CHECKS_PER_CHECKLIST,
  SEVERITIES,
  type AuditSeverity,
  type Checklist,
} from "@/lib/audits";

// Each row needs a stable key while checks are added, removed and reordered
type DraftCheck = { key: number; title: string; requirement: string; severity: AuditSeverity };

let nextKey = 0;
const emptyCheck = (): DraftCheck => ({ key: nextKey++, title: "", requirement: "", severity: "Medium" });

export function ChecklistEditor({ checklist }: { checklist: Checklist | null }) {
  const [name, setName] = useState(checklist?.name ?? "");
  const [description, setDescription] = useState(checklist?.description ?? "");
  const [checks, setChecks] = useState<DraftCheck[]>(() =>
    checklist ? checklist.checks.map((c) => ({ key: nextKey++, title: c.title, requirement: c.requirement, severity: c.severity })) : [emptyCheck()],
  );
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();

  const update = (key: number, change: Partial<DraftCheck>) =>
    setChecks((current) => current.map((c) => (c.key === key ? { ...c, ...change } : c)));

  function move(index: number, offset: -1 | 1) {
    setChecks((current) => {
      const next = [...current];
      [next[index], next[index + offset]] = [next[index + offset], next[index]];
      return next;
    });
  }

  function save(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    startTransition(async () => {
      // Redirects back to the audits page on success
      const result = await saveChecklistAction(checklist?.id ?? null, {
        name,
        description,
        checks: checks.map(({ title, requirement, severity }) => ({ title, requirement, severity })),
      });
      if (!result.ok) setError(result.error);
    });
  }

  function remove() {
    if (!checklist || !window.confirm(`Delete "${checklist.name}"? Existing audit reports are kept.`)) return;
    startTransition(async () => {
      const result = await deleteChecklistAction(checklist.id);
      if (!result.ok) setError(result.error);
    });
  }

  return (
    <form onSubmit={save} className="flex flex-col gap-6">
      <section className="flex flex-col gap-3 rounded-2xl border border-zinc-200 bg-white p-5 dark:border-zinc-800 dark:bg-zinc-900">
        <label className="flex flex-col gap-1 text-sm font-medium text-zinc-700 dark:text-zinc-300">
          Name
          <input value={name} onChange={(event) => setName(event.target.value)} required maxLength={MAX_CHECKLIST_NAME} className={inputClass} />
        </label>
        <label className="flex flex-col gap-1 text-sm font-medium text-zinc-700 dark:text-zinc-300">
          Description <span className="font-normal text-zinc-500">(optional)</span>
          <input
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            maxLength={MAX_CHECKLIST_DESCRIPTION}
            className={inputClass}
          />
        </label>
      </section>

      <section className="flex flex-col gap-3">
        <div>
          <h2 className="font-semibold text-zinc-900 dark:text-zinc-100">
            Checks ({checks.length}/{MAX_CHECKS_PER_CHECKLIST})
          </h2>
          <p className="text-sm text-zinc-500">
            Write each requirement as what the document must say, with specific numbers where they matter
            (for example &quot;at least 60 days&apos; notice&quot;).
          </p>
        </div>

        <ol className="flex flex-col gap-3">
          {checks.map((check, index) => (
            <li key={check.key} className="flex flex-col gap-2 rounded-2xl border border-zinc-200 bg-white p-4 dark:border-zinc-800 dark:bg-zinc-900">
              <div className="flex flex-col gap-2 sm:flex-row">
                <input
                  value={check.title}
                  onChange={(event) => update(check.key, { title: event.target.value })}
                  placeholder={`Check ${index + 1} title, e.g. Limitation of liability`}
                  aria-label={`Check ${index + 1} title`}
                  required
                  maxLength={MAX_CHECK_TITLE}
                  className={`min-w-0 flex-1 ${inputClass}`}
                />
                <select
                  value={check.severity}
                  onChange={(event) => update(check.key, { severity: event.target.value as AuditSeverity })}
                  aria-label={`Check ${index + 1} severity`}
                  className={inputClass}
                >
                  {SEVERITIES.map((severity) => (
                    <option key={severity} value={severity}>
                      {severity} severity
                    </option>
                  ))}
                </select>
              </div>
              <textarea
                value={check.requirement}
                onChange={(event) => update(check.key, { requirement: event.target.value })}
                placeholder="The requirement the document must meet"
                aria-label={`Check ${index + 1} requirement`}
                required
                rows={2}
                maxLength={MAX_CHECK_REQUIREMENT}
                className={inputClass}
              />
              <div className="flex justify-end gap-1 text-zinc-600 dark:text-zinc-300">
                <button type="button" onClick={() => move(index, -1)} disabled={index === 0} className={smallButton}>
                  Move up
                </button>
                <button type="button" onClick={() => move(index, 1)} disabled={index === checks.length - 1} className={smallButton}>
                  Move down
                </button>
                <button
                  type="button"
                  onClick={() => setChecks((current) => current.filter((c) => c.key !== check.key))}
                  disabled={checks.length === 1}
                  className={`${smallButton} text-red-600 dark:text-red-400`}
                >
                  Remove
                </button>
              </div>
            </li>
          ))}
        </ol>

        <button
          type="button"
          onClick={() => setChecks((current) => [...current, emptyCheck()])}
          disabled={checks.length >= MAX_CHECKS_PER_CHECKLIST}
          className="self-start rounded-lg border border-dashed border-zinc-300 px-4 py-2 text-sm font-medium text-zinc-700 hover:bg-zinc-50 disabled:opacity-50 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-800"
        >
          Add check
        </button>
      </section>

      <div className="flex flex-wrap items-center gap-3">
        <button type="submit" disabled={pending} className={primaryButton}>
          {pending ? "Saving…" : "Save checklist"}
        </button>
        {checklist ? (
          <button type="button" onClick={remove} disabled={pending} className={`${smallButton} text-red-600 dark:text-red-400`}>
            Delete checklist
          </button>
        ) : null}
        {error ? <p className="text-sm text-red-600 dark:text-red-400">{error}</p> : null}
      </div>
    </form>
  );
}
