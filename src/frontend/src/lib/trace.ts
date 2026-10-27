import "server-only";

import { headers } from "next/headers";

export const CORRELATION_HEADER = "X-Correlation-Id";

// Headers that let the API rate-limit by the real client address and tie its logs and
// background jobs to the browser request that caused them.
export async function forwardingHeaders(): Promise<Record<string, string>> {
  const result: Record<string, string> = {};
  try {
    const incoming = await headers();
    const forwardedFor = incoming.get("x-forwarded-for") ?? incoming.get("x-real-ip");
    if (forwardedFor) result["X-Forwarded-For"] = forwardedFor;
    const proto = incoming.get("x-forwarded-proto");
    if (proto) result["X-Forwarded-Proto"] = proto;
    result[CORRELATION_HEADER] = incoming.get(CORRELATION_HEADER.toLowerCase()) ?? crypto.randomUUID();
  } catch {
    // Outside a request (e.g. during build) there is nothing to forward
    result[CORRELATION_HEADER] = crypto.randomUUID();
  }
  return result;
}
