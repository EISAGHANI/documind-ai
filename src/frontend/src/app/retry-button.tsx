"use client";

import { useTransition } from "react";

import { reprocessDocumentAction } from "./actions";

export function RetryButton({ id }: { id: string }) {
  const [pending, startTransition] = useTransition();

  function handleClick() {
    startTransition(async () => {
      const result = await reprocessDocumentAction(id);
      if (result.error) window.alert(result.error);
    });
  }

  return (
    <button
      type="button"
      onClick={handleClick}
      disabled={pending}
      className="rounded-md px-2 py-1 text-sm text-indigo-600 transition-colors hover:bg-indigo-50 disabled:opacity-50 dark:text-indigo-400 dark:hover:bg-indigo-950/40"
    >
      {pending ? "Retrying…" : "Retry"}
    </button>
  );
}
