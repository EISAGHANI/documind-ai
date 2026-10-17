import type { Metadata } from "next";

import { SetupBanner } from "../setup-banner";
import { ChatPanel } from "./chat-panel";
import { requireUser } from "@/lib/auth";

export const metadata: Metadata = {
  title: "Ask · DocuMind AI",
};

export default async function ChatPage() {
  await requireUser();
  return (
    <main className="mx-auto flex w-full max-w-4xl flex-1 flex-col gap-4 px-4 py-8 sm:px-6">
      <SetupBanner needs="chat" />
      <ChatPanel />
    </main>
  );
}
