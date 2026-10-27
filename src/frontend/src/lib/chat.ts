// Types and stream parsing for the chat endpoint; shared by client components

export type ChatSource = {
  index: number;
  documentId: string;
  documentTitle: string;
  pageNumber: number | null;
  excerpt: string;
};

// One text block of an answer plus the sources Claude cited for it
export type AnswerSegment = { text: string; citations: number[] };

export type ChatStreamEvent =
  | { type: "conversation"; conversationId: string }
  | { type: "sources"; sources: ChatSource[] }
  | { type: "block" }
  | { type: "text"; text: string }
  | { type: "citation"; sourceIndex: number; citedText: string }
  | { type: "done"; stopReason: string | null }
  | { type: "error"; message: string };

// Parses a server-sent event stream into typed events
export async function* readChatStream(body: ReadableStream<Uint8Array>): AsyncGenerator<ChatStreamEvent> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  while (true) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });

    let boundary: number;
    while ((boundary = buffer.indexOf("\n\n")) !== -1) {
      const raw = buffer.slice(0, boundary);
      buffer = buffer.slice(boundary + 2);

      let name = "message";
      let data = "";
      for (const line of raw.split("\n")) {
        if (line.startsWith("event:")) name = line.slice(6).trim();
        else if (line.startsWith("data:")) data += line.slice(5).trim();
      }

      const payload = data ? JSON.parse(data) : {};
      switch (name) {
        case "conversation":
          yield { type: "conversation", conversationId: payload.conversationId };
          break;
        case "sources":
          yield { type: "sources", sources: payload };
          break;
        case "block":
          yield { type: "block" };
          break;
        case "text":
          yield { type: "text", text: payload.text };
          break;
        case "citation":
          yield { type: "citation", sourceIndex: payload.sourceIndex, citedText: payload.citedText };
          break;
        case "done":
          yield { type: "done", stopReason: payload.stopReason ?? null };
          break;
        case "error":
          yield { type: "error", message: payload.message };
          break;
      }
    }
  }
}
