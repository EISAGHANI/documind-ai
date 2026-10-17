"use client";

import { useEffect, useRef, useState } from "react";

import { readChatStream, type AnswerSegment, type ChatSource } from "@/lib/chat";

type UserMessage = { role: "user"; content: string };
type AssistantMessage = {
  role: "assistant";
  segments: AnswerSegment[];
  sources: ChatSource[];
  status: "streaming" | "done" | "error";
  notice?: string;
};
type Message = UserMessage | AssistantMessage;

const answerText = (message: AssistantMessage) => message.segments.map((s) => s.text).join("");

function stopReasonNotice(stopReason: string | null): string | undefined {
  if (stopReason === "refusal") return "Claude declined to answer this request.";
  if (stopReason === "max_tokens") return "The answer was cut off because it reached the length limit.";
  return undefined;
}

export function ChatPanel() {
  const [messages, setMessages] = useState<Message[]>([]);
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);
  const abortRef = useRef<AbortController | null>(null);
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth", block: "end" });
  }, [messages]);

  // Applies an update to the assistant message currently being streamed (always the last one)
  function updateLast(update: (message: AssistantMessage) => AssistantMessage) {
    setMessages((current) => {
      const last = current[current.length - 1];
      if (!last || last.role !== "assistant") return current;
      return [...current.slice(0, -1), update(last)];
    });
  }

  async function send() {
    const question = input.trim();
    if (!question || busy) return;

    // Send prior turns as plain text; the API attaches search results to the new question
    const history = messages
      .filter((m) => m.role === "user" || m.status === "done")
      .map((m) => (m.role === "user" ? m : { role: "assistant" as const, content: answerText(m) }))
      .filter((m) => m.content.length > 0);

    setInput("");
    setBusy(true);
    setMessages((current) => [
      ...current,
      { role: "user", content: question },
      { role: "assistant", segments: [], sources: [], status: "streaming" },
    ]);

    const controller = new AbortController();
    abortRef.current = controller;

    try {
      const response = await fetch("/api/chat", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ messages: [...history, { role: "user", content: question }] }),
        signal: controller.signal,
      });

      if (!response.ok || !response.body) {
        let detail = "The question could not be sent.";
        try {
          const problem = await response.json();
          detail = problem.detail ?? problem.title ?? detail;
        } catch {
          // Not JSON; keep the generic message
        }
        updateLast((m) => ({ ...m, status: "error", notice: detail }));
        return;
      }

      for await (const event of readChatStream(response.body)) {
        switch (event.type) {
          case "sources":
            updateLast((m) => ({ ...m, sources: event.sources }));
            break;
          case "block":
            updateLast((m) => ({ ...m, segments: [...m.segments, { text: "", citations: [] }] }));
            break;
          case "text":
            updateLast((m) => {
              const segments = m.segments.length ? [...m.segments] : [{ text: "", citations: [] }];
              const last = segments[segments.length - 1];
              segments[segments.length - 1] = { ...last, text: last.text + event.text };
              return { ...m, segments };
            });
            break;
          case "citation":
            updateLast((m) => {
              const segments = m.segments.length ? [...m.segments] : [{ text: "", citations: [] }];
              const last = segments[segments.length - 1];
              if (!last.citations.includes(event.sourceIndex)) {
                segments[segments.length - 1] = { ...last, citations: [...last.citations, event.sourceIndex] };
              }
              return { ...m, segments };
            });
            break;
          case "done":
            updateLast((m) => ({
              ...m,
              // A refusal can arrive after partial output; that partial answer must not be shown as complete
              segments: event.stopReason === "refusal" ? [] : m.segments,
              status: "done",
              notice: stopReasonNotice(event.stopReason),
            }));
            break;
          case "error":
            updateLast((m) => ({ ...m, status: "error", notice: event.message }));
            break;
        }
      }

      // Stream ended without a done/error event (e.g. the connection dropped)
      updateLast((m) => (m.status === "streaming" ? { ...m, status: "error", notice: "The answer was interrupted." } : m));
    } catch (error) {
      const stopped = error instanceof DOMException && error.name === "AbortError";
      updateLast((m) => ({
        ...m,
        status: stopped ? "done" : "error",
        notice: stopped ? "Stopped." : "Could not reach DocuMind. Is the server running?",
      }));
    } finally {
      abortRef.current = null;
      setBusy(false);
    }
  }

  function handleKeyDown(event: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      void send();
    }
  }

  return (
    <div className="flex flex-1 flex-col gap-4">
      <div className="flex flex-1 flex-col gap-6">
        {messages.length === 0 ? (
          <div className="rounded-2xl border border-zinc-200 bg-white p-6 dark:border-zinc-800 dark:bg-zinc-900">
            <h1 className="text-xl font-semibold text-zinc-900 dark:text-zinc-100">Ask your documents</h1>
            <p className="mt-1 text-sm text-zinc-500">
              Answers come only from your processed documents, with citations to the page they came from.
            </p>
          </div>
        ) : null}

        {messages.map((message, i) =>
          message.role === "user" ? (
            <div key={i} className="self-end max-w-[85%] rounded-2xl bg-indigo-600 px-4 py-2.5 text-sm text-white whitespace-pre-wrap">
              {message.content}
            </div>
          ) : (
            <AssistantBubble key={i} message={message} />
          ),
        )}
        <div ref={bottomRef} />
      </div>

      <div className="sticky bottom-4 flex gap-2 rounded-2xl border border-zinc-200 bg-white p-2 shadow-sm dark:border-zinc-800 dark:bg-zinc-900">
        <textarea
          value={input}
          onChange={(event) => setInput(event.target.value)}
          onKeyDown={handleKeyDown}
          rows={2}
          maxLength={4000}
          placeholder="Ask about your contracts, policies or manuals…"
          className="flex-1 resize-none bg-transparent px-2 py-1.5 text-sm text-zinc-900 outline-none placeholder:text-zinc-400 dark:text-zinc-100"
        />
        {busy ? (
          <button
            type="button"
            onClick={() => abortRef.current?.abort()}
            className="self-end rounded-lg border border-zinc-300 px-4 py-2 text-sm font-medium text-zinc-700 hover:bg-zinc-50 dark:border-zinc-700 dark:text-zinc-200 dark:hover:bg-zinc-800"
          >
            Stop
          </button>
        ) : (
          <button
            type="button"
            onClick={() => void send()}
            disabled={!input.trim()}
            className="self-end rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-500 disabled:cursor-not-allowed disabled:opacity-50"
          >
            Send
          </button>
        )}
      </div>
    </div>
  );
}

function AssistantBubble({ message }: { message: AssistantMessage }) {
  const text = answerText(message);
  // Only list sources Claude actually cited; fall back to all retrieved ones if it cited none
  const citedIndexes = new Set(message.segments.flatMap((s) => s.citations));
  const shownSources = citedIndexes.size
    ? message.sources.filter((s) => citedIndexes.has(s.index))
    : message.status === "done" ? message.sources : [];

  return (
    <div className="max-w-[85%] rounded-2xl border border-zinc-200 bg-white px-4 py-3 dark:border-zinc-800 dark:bg-zinc-900">
      {text ? (
        <p className="text-sm leading-6 text-zinc-800 whitespace-pre-wrap dark:text-zinc-200">
          {message.segments.map((segment, i) => (
            <span key={i}>
              {segment.text}
              {segment.citations.map((sourceIndex) => (
                <CitationMarker key={sourceIndex} source={message.sources[sourceIndex]} />
              ))}
            </span>
          ))}
        </p>
      ) : message.status === "streaming" ? (
        <p className="text-sm text-zinc-500">{message.sources.length ? "Reading your documents…" : "Searching…"}</p>
      ) : null}

      {message.notice ? (
        <p className={`mt-2 text-sm ${message.status === "error" ? "text-red-600 dark:text-red-400" : "text-zinc-500"}`}>
          {message.notice}
        </p>
      ) : null}

      {shownSources.length ? (
        <ol className="mt-3 flex flex-col gap-1 border-t border-zinc-200 pt-3 text-xs dark:border-zinc-800">
          {shownSources.map((source) => (
            <li key={source.index} className="text-zinc-500">
              <span className="font-medium text-zinc-700 dark:text-zinc-300">[{source.index + 1}]</span>{" "}
              <a href={sourceHref(source)} target="_blank" rel="noreferrer" className="hover:text-indigo-600 hover:underline">
                {source.documentTitle}
                {source.pageNumber ? `, page ${source.pageNumber}` : ""}
              </a>
            </li>
          ))}
        </ol>
      ) : null}
    </div>
  );
}

function CitationMarker({ source }: { source: ChatSource | undefined }) {
  if (!source) return null;
  return (
    <a
      href={sourceHref(source)}
      target="_blank"
      rel="noreferrer"
      title={`${source.documentTitle}${source.pageNumber ? `, page ${source.pageNumber}` : ""}\n\n${source.excerpt}`}
      className="ml-0.5 align-super text-[10px] font-semibold text-indigo-600 hover:underline dark:text-indigo-400"
    >
      [{source.index + 1}]
    </a>
  );
}

// PDF viewers jump to the page in the #page fragment
function sourceHref(source: ChatSource) {
  return `/documents/${source.documentId}/file?inline=1${source.pageNumber ? `#page=${source.pageNumber}` : ""}`;
}
