import type { Metadata } from "next";

import { ChatPanel } from "./chat-panel";

export const metadata: Metadata = {
  title: "Ask · DocuMind AI",
};

export default function ChatPage() {
  return (
    <main className="mx-auto flex w-full max-w-4xl flex-1 flex-col px-4 py-8 sm:px-6">
      <ChatPanel />
    </main>
  );
}
