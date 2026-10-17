import type { NextRequest } from "next/server";

import { apiFetch } from "@/lib/api";

const MAX_BODY_BYTES = 1024 * 1024;

// Forwards the chat request to the API (adding the tenant header) and streams the
// server-sent events straight back to the browser
export async function POST(request: NextRequest) {
  const body = await request.text();
  if (body.length > MAX_BODY_BYTES) {
    return Response.json({ detail: "The conversation is too long." }, { status: 413 });
  }

  let upstream: Response;
  try {
    upstream = await apiFetch("/api/chat", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body,
      signal: request.signal,
    });
  } catch {
    return Response.json({ detail: "Could not reach the DocuMind API. Is the backend running?" }, { status: 502 });
  }

  if (!upstream.ok || !upstream.body) {
    const detail = await upstream.text();
    return new Response(detail || "Chat request failed.", {
      status: upstream.status,
      headers: { "Content-Type": upstream.headers.get("content-type") ?? "text/plain" },
    });
  }

  return new Response(upstream.body, {
    headers: {
      "Content-Type": "text/event-stream",
      "Cache-Control": "no-cache, no-transform",
      "X-Accel-Buffering": "no",
    },
  });
}
