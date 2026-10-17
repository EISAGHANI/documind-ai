"use client";

import { useTransition } from "react";

import { deleteDocumentAction } from "./actions";

export function DeleteButton({ id, title }: { id: string; title: string }) {
  const [pending, startTransition] = useTransition();

  function handleClick() {
    if (!window.confirm(`Delete "${title}"? This cannot be undone.`)) return;

    startTransition(async () => {
      const result = await deleteDocumentAction(id);
      if (result.error) window.alert(result.error);
    });
  }

  return (
    <button
      type="button"
      onClick={handleClick}
      disabled={pending}
      className="rounded-md px-2 py-1 text-sm text-red-600 transition-colors hover:bg-red-50 disabled:opacity-50 dark:text-red-400 dark:hover:bg-red-950/40"
    >
      {pending ? "Deleting…" : "Delete"}
    </button>
  );
}
