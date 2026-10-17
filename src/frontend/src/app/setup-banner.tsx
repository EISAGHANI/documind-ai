import Link from "next/link";

import { getAiSettings } from "@/lib/api";
import { getCurrentUser } from "@/lib/auth";
import { isReadyForChat } from "@/lib/settings";

// Tells the user which keys are still missing for the current page to work. Admins get a link to
// Settings; Members are told to ask an admin, since they can't change keys.
export async function SetupBanner({ needs }: { needs: "indexing" | "chat" }) {
  let missing: string | null = null;
  let isAdmin = false;
  try {
    const [settings, user] = await Promise.all([getAiSettings(), getCurrentUser()]);
    isAdmin = user?.role === "Admin";
    const voyage = settings.providers.find((p) => p.provider === "voyage");
    if (!voyage?.configured) {
      missing = "A Voyage AI API key is needed so documents can be indexed and searched.";
    } else if (needs === "chat" && !isReadyForChat(settings)) {
      missing = "A Claude, OpenAI or Gemini API key (with a model) is needed to answer questions.";
    }
  } catch {
    // The page shows its own error when the API is unreachable
    return null;
  }

  if (!missing) return null;

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950/40 dark:text-amber-200">
      <span>
        {missing}
        {isAdmin ? "" : " Ask a company admin to add it."}
      </span>
      {isAdmin ? (
        <Link href="/settings" className="font-medium underline underline-offset-2">
          Open Settings
        </Link>
      ) : null}
    </div>
  );
}
