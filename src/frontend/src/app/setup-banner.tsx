import Link from "next/link";

import { getAiSettings } from "@/lib/api";
import { isReadyForChat } from "@/lib/settings";

// Tells the user which keys are still missing for the current page to work
export async function SetupBanner({ needs }: { needs: "indexing" | "chat" }) {
  let message: string | null = null;
  try {
    const settings = await getAiSettings();
    const voyage = settings.providers.find((p) => p.provider === "voyage");
    if (!voyage?.configured) {
      message = "Add your Voyage AI API key so documents can be indexed and searched.";
    } else if (needs === "chat" && !isReadyForChat(settings)) {
      message = "Add a Claude, OpenAI or Gemini API key and choose a model to start asking questions.";
    }
  } catch {
    // The page shows its own error when the API is unreachable
    return null;
  }

  if (!message) return null;

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950/40 dark:text-amber-200">
      <span>{message}</span>
      <Link href="/settings" className="font-medium underline underline-offset-2">
        Open Settings
      </Link>
    </div>
  );
}
