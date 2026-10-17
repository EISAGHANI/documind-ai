"use client";

import { startTransition, useActionState, useRef, useState } from "react";

import { uploadDocumentAction, type UploadState } from "./actions";
import { formatBytes, MAX_FILE_SIZE_BYTES } from "@/lib/documents";

const initialState: UploadState = { status: "idle" };

export function UploadForm() {
  const [file, setFile] = useState<File | null>(null);
  const [clientError, setClientError] = useState<string | null>(null);
  const [dragging, setDragging] = useState(false);
  const formRef = useRef<HTMLFormElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const [state, formAction, pending] = useActionState(async (prev: UploadState, formData: FormData) => {
    const result = await uploadDocumentAction(prev, formData);
    // Clear the form only after a successful upload, so a failed one can be retried
    if (result.status === "success") {
      formRef.current?.reset();
      setFile(null);
    }
    return result;
  }, initialState);

  // Submitting manually (instead of <form action>) stops React from resetting the form on every submit
  function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const formData = new FormData(event.currentTarget);
    startTransition(() => formAction(formData));
  }

  function selectFile(selected: File | null) {
    setClientError(null);
    if (!selected) {
      setFile(null);
      return;
    }
    if (selected.type !== "application/pdf" && !selected.name.toLowerCase().endsWith(".pdf")) {
      setClientError("Only PDF files are supported.");
      setFile(null);
      return;
    }
    if (selected.size > MAX_FILE_SIZE_BYTES) {
      setClientError("Files larger than 50 MB are not allowed.");
      setFile(null);
      return;
    }
    setFile(selected);
  }

  function handleDrop(event: React.DragEvent<HTMLLabelElement>) {
    event.preventDefault();
    setDragging(false);
    const dropped = event.dataTransfer.files[0] ?? null;
    if (dropped && inputRef.current) {
      // Put the dropped file into the real input so it is submitted with the form
      const transfer = new DataTransfer();
      transfer.items.add(dropped);
      inputRef.current.files = transfer.files;
    }
    selectFile(dropped);
  }

  const error = clientError ?? (state.status === "error" ? state.message : null);

  return (
    <form ref={formRef} onSubmit={handleSubmit} className="flex flex-col gap-4">
      <label
        htmlFor="file"
        onDragOver={(event) => {
          event.preventDefault();
          setDragging(true);
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={handleDrop}
        className={`flex cursor-pointer flex-col items-center justify-center gap-1 rounded-xl border-2 border-dashed px-6 py-10 text-center transition-colors ${
          dragging
            ? "border-indigo-500 bg-indigo-50 dark:bg-indigo-950/40"
            : "border-zinc-300 hover:border-zinc-400 dark:border-zinc-700 dark:hover:border-zinc-500"
        }`}
      >
        {file ? (
          <>
            <span className="font-medium text-zinc-900 dark:text-zinc-100">{file.name}</span>
            <span className="text-sm text-zinc-500">{formatBytes(file.size)} · click to choose another</span>
          </>
        ) : (
          <>
            <span className="font-medium text-zinc-900 dark:text-zinc-100">Drop a PDF here or click to browse</span>
            <span className="text-sm text-zinc-500">PDF only, up to 50 MB</span>
          </>
        )}
        <input
          ref={inputRef}
          id="file"
          name="file"
          type="file"
          accept="application/pdf,.pdf"
          className="sr-only"
          onChange={(event) => selectFile(event.target.files?.[0] ?? null)}
        />
      </label>

      <div className="flex flex-col gap-3 sm:flex-row">
        <input
          name="title"
          type="text"
          maxLength={255}
          placeholder="Title (optional, defaults to the file name)"
          className="flex-1 rounded-lg border border-zinc-300 bg-white px-3 py-2 text-sm text-zinc-900 outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500/20 dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-100"
        />
        <button
          type="submit"
          disabled={!file || pending}
          className="rounded-lg bg-indigo-600 px-5 py-2 text-sm font-medium text-white transition-colors hover:bg-indigo-500 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {pending ? "Uploading…" : "Upload"}
        </button>
      </div>

      <p aria-live="polite" className="min-h-5 text-sm">
        {error ? (
          <span className="text-red-600 dark:text-red-400">{error}</span>
        ) : state.status === "success" ? (
          <span className="text-emerald-600 dark:text-emerald-400">{state.message}</span>
        ) : null}
      </p>
    </form>
  );
}
