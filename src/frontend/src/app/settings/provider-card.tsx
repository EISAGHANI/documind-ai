"use client";

import { useState, useTransition } from "react";

import {
  deleteApiKeyAction,
  listModelsAction,
  saveApiKeyAction,
  setChatProviderAction,
  setModelAction,
} from "./actions";
import { PROVIDER_KEY_URLS, type AiProviderStatus } from "@/lib/settings";

type Message = { kind: "success" | "error"; text: string } | null;

export function ProviderCard({ status, active }: { status: AiProviderStatus; active: boolean }) {
  const isChat = status.purpose === "chat";
  const [apiKey, setApiKey] = useState("");
  const [editing, setEditing] = useState(!status.configured);
  const [models, setModels] = useState<string[] | null>(null);
  const [message, setMessage] = useState<Message>(null);
  const [pending, startTransition] = useTransition();

  function run(task: () => Promise<Message>) {
    setMessage(null);
    startTransition(async () => setMessage(await task()));
  }

  function save() {
    run(async () => {
      const result = await saveApiKeyAction(status.provider, apiKey);
      if (!result.ok) return { kind: "error", text: result.error };
      setApiKey("");
      setEditing(false);
      if (isChat) setModels(result.data ?? []);
      return { kind: "success", text: "Key verified and saved." };
    });
  }

  function loadModels() {
    run(async () => {
      const result = await listModelsAction(status.provider);
      if (!result.ok) return { kind: "error", text: result.error };
      setModels(result.data ?? []);
      return null;
    });
  }

  function chooseModel(model: string) {
    run(async () => {
      const result = await setModelAction(status.provider, model);
      return result.ok ? { kind: "success", text: `Using ${model}.` } : { kind: "error", text: result.error };
    });
  }

  function makeActive() {
    run(async () => {
      const result = await setChatProviderAction(status.provider);
      return result.ok ? null : { kind: "error", text: result.error };
    });
  }

  function remove() {
    if (!window.confirm(`Remove the saved ${status.displayName} key?`)) return;
    run(async () => {
      const result = await deleteApiKeyAction(status.provider);
      if (!result.ok) return { kind: "error", text: result.error };
      setEditing(true);
      setModels(null);
      return { kind: "success", text: "Key removed." };
    });
  }

  // Show the current model even before the full list has been loaded
  const modelOptions = models ?? (status.model ? [status.model] : []);

  return (
    <div
      className={`rounded-2xl border bg-white p-5 dark:bg-zinc-900 ${
        active ? "border-indigo-500 ring-1 ring-indigo-500" : "border-zinc-200 dark:border-zinc-800"
      }`}
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <div className="flex items-center gap-2">
            <h3 className="font-semibold text-zinc-900 dark:text-zinc-100">{status.displayName}</h3>
            {active ? (
              <span className="rounded-full bg-indigo-100 px-2 py-0.5 text-xs font-medium text-indigo-700 dark:bg-indigo-950/60 dark:text-indigo-300">
                Answers questions
              </span>
            ) : null}
          </div>
          <p className="mt-0.5 text-sm text-zinc-500">
            {status.configured ? (
              <>
                Key saved <span className="font-mono">{status.keyHint}</span>
                {status.updatedAt ? ` · ${new Date(status.updatedAt).toLocaleDateString()}` : ""}
              </>
            ) : (
              <>
                No key saved.{" "}
                <a href={PROVIDER_KEY_URLS[status.provider]} target="_blank" rel="noreferrer" className="text-indigo-600 hover:underline dark:text-indigo-400">
                  Get a key
                </a>
              </>
            )}
          </p>
        </div>

        {status.configured ? (
          <div className="flex gap-2">
            {isChat && !active ? (
              <button type="button" onClick={makeActive} disabled={pending || !status.model} title={status.model ? undefined : "Choose a model first"} className={secondaryButton}>
                Use for answers
              </button>
            ) : null}
            {!editing ? (
              <button type="button" onClick={() => setEditing(true)} disabled={pending} className={secondaryButton}>
                Replace key
              </button>
            ) : null}
            <button type="button" onClick={remove} disabled={pending} className={`${secondaryButton} text-red-600 dark:text-red-400`}>
              Remove
            </button>
          </div>
        ) : null}
      </div>

      {editing ? (
        <form
          className="mt-4 flex flex-col gap-2 sm:flex-row"
          onSubmit={(event) => {
            event.preventDefault();
            save();
          }}
        >
          <input
            type="password"
            value={apiKey}
            onChange={(event) => setApiKey(event.target.value)}
            placeholder={`Paste your ${status.displayName} API key`}
            autoComplete="off"
            spellCheck={false}
            maxLength={500}
            className="flex-1 rounded-lg border border-zinc-300 bg-white px-3 py-2 font-mono text-sm text-zinc-900 outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500/20 dark:border-zinc-700 dark:bg-zinc-950 dark:text-zinc-100"
          />
          <button type="submit" disabled={pending || apiKey.trim().length < 8} className={primaryButton}>
            {pending ? "Checking…" : "Save & verify"}
          </button>
          {status.configured ? (
            <button type="button" onClick={() => setEditing(false)} disabled={pending} className={secondaryButton}>
              Cancel
            </button>
          ) : null}
        </form>
      ) : null}

      {isChat && status.configured ? (
        <div className="mt-4 flex flex-wrap items-center gap-2 text-sm">
          <label htmlFor={`${status.provider}-model`} className="text-zinc-600 dark:text-zinc-400">
            Model
          </label>
          <select
            id={`${status.provider}-model`}
            value={status.model ?? ""}
            onFocus={() => models === null && !pending && loadModels()}
            onChange={(event) => event.target.value && chooseModel(event.target.value)}
            disabled={pending}
            className="min-w-64 rounded-lg border border-zinc-300 bg-white px-3 py-1.5 text-zinc-900 dark:border-zinc-700 dark:bg-zinc-950 dark:text-zinc-100"
          >
            {!status.model ? <option value="">Choose a model…</option> : null}
            {modelOptions.map((model) => (
              <option key={model} value={model}>
                {model}
              </option>
            ))}
          </select>
          {models === null ? (
            <button type="button" onClick={loadModels} disabled={pending} className="text-indigo-600 hover:underline disabled:opacity-50 dark:text-indigo-400">
              {pending ? "Loading…" : "Show all models"}
            </button>
          ) : null}
        </div>
      ) : null}

      {!isChat && status.model ? (
        <p className="mt-3 text-sm text-zinc-500">
          Model: <span className="font-mono">{status.model}</span>
        </p>
      ) : null}

      {message ? (
        <p aria-live="polite" className={`mt-3 text-sm ${message.kind === "error" ? "text-red-600 dark:text-red-400" : "text-emerald-600 dark:text-emerald-400"}`}>
          {message.text}
        </p>
      ) : null}
    </div>
  );
}

const primaryButton =
  "rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-indigo-500 disabled:cursor-not-allowed disabled:opacity-50";
const secondaryButton =
  "rounded-lg border border-zinc-300 px-3 py-1.5 text-sm font-medium text-zinc-700 transition-colors hover:bg-zinc-50 disabled:cursor-not-allowed disabled:opacity-50 dark:border-zinc-700 dark:text-zinc-200 dark:hover:bg-zinc-800";
