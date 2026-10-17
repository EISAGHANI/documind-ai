import type { NextRequest } from "next/server";

import { apiFetch } from "@/lib/api";

// Streams the original file from the API so the browser never needs the tenant header
export async function GET(_request: NextRequest, ctx: RouteContext<"/documents/[id]/file">) {
  const { id } = await ctx.params;
  const upstream = await apiFetch(`/api/documents/${encodeURIComponent(id)}/file`);

  if (!upstream.ok || !upstream.body) {
    return new Response("Document not found.", { status: upstream.status === 404 ? 404 : 502 });
  }

  const headers = new Headers();
  for (const name of ["content-type", "content-length", "content-disposition"]) {
    const value = upstream.headers.get(name);
    if (value) headers.set(name, value);
  }

  return new Response(upstream.body, { status: 200, headers });
}
