import { describe, expect, it } from "vitest";

import { safeNextPath } from "./auth-types";
import { readChatStream, type ChatStreamEvent } from "./chat";

describe("safeNextPath", () => {
  it("keeps same-site paths", () => {
    expect(safeNextPath("/documents?x=1")).toBe("/documents?x=1");
  });

  it.each(["//evil.example", "/\\evil.example", "https://evil.example", "", undefined, 42])(
    "falls back to / for %j",
    (value) => {
      expect(safeNextPath(value)).toBe("/");
    },
  );
});

function streamOf(...chunks: string[]): ReadableStream<Uint8Array> {
  const encoder = new TextEncoder();
  return new ReadableStream({
    start(controller) {
      for (const chunk of chunks) controller.enqueue(encoder.encode(chunk));
      controller.close();
    },
  });
}

async function collect(body: ReadableStream<Uint8Array>): Promise<ChatStreamEvent[]> {
  const events: ChatStreamEvent[] = [];
  for await (const event of readChatStream(body)) events.push(event);
  return events;
}

describe("readChatStream", () => {
  it("parses events even when they are split across network chunks", async () => {
    const events = await collect(
      streamOf('event: text\ndata: {"te', 'xt":"Hello"}\n\nevent: done\ndata: {"stopReason":"end_turn"}\n\n'),
    );

    expect(events[0]).toEqual({ type: "text", text: "Hello" });
    expect(events[1]).toMatchObject({ type: "done" });
  });

  it("surfaces error events", async () => {
    const events = await collect(streamOf('event: error\ndata: {"message":"The AI provider is busy"}\n\n'));

    expect(events).toEqual([{ type: "error", message: "The AI provider is busy" }]);
  });
});
