import type { Metadata } from "next";

import { ProviderCard } from "./provider-card";
import { getAiSettings } from "@/lib/api";
import type { AiSettings } from "@/lib/settings";

export const metadata: Metadata = {
  title: "Settings · DocuMind AI",
};

export default async function SettingsPage() {
  let settings: AiSettings;
  try {
    settings = await getAiSettings();
  } catch {
    return (
      <main className="mx-auto w-full max-w-4xl px-4 py-8 sm:px-6">
        <p className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950/40 dark:text-red-300">
          Could not load settings. Make sure the DocuMind API and database are running.
        </p>
      </main>
    );
  }

  const chatProviders = settings.providers.filter((p) => p.purpose === "chat");
  const embeddings = settings.providers.filter((p) => p.purpose === "embeddings");

  return (
    <main className="mx-auto flex w-full max-w-4xl flex-col gap-8 px-4 py-8 sm:px-6">
      <div>
        <h1 className="text-xl font-semibold text-zinc-900 dark:text-zinc-100">AI provider keys</h1>
        <p className="mt-1 max-w-2xl text-sm text-zinc-500">
          DocuMind uses your own API keys, so usage is billed to your accounts. Keys are encrypted before
          they are stored and are never shown again; only the last four characters are displayed.
        </p>
      </div>

      <section className="flex flex-col gap-3">
        <div>
          <h2 className="font-semibold text-zinc-900 dark:text-zinc-100">Answering questions</h2>
          <p className="text-sm text-zinc-500">
            Add a key for at least one provider and choose which one answers. Claude gives the most precise citations.
          </p>
        </div>
        {chatProviders.map((status) => (
          <ProviderCard key={status.provider} status={status} active={settings.chatProvider === status.provider} />
        ))}
      </section>

      <section className="flex flex-col gap-3">
        <div>
          <h2 className="font-semibold text-zinc-900 dark:text-zinc-100">Indexing documents (required)</h2>
          <p className="text-sm text-zinc-500">
            Voyage AI turns documents and questions into vectors for search. The model is fixed so your index
            stays consistent.
          </p>
        </div>
        {embeddings.map((status) => (
          <ProviderCard key={status.provider} status={status} active={false} />
        ))}
      </section>
    </main>
  );
}
