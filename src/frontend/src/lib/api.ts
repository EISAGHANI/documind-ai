import "server-only";

import type { DocumentSummary } from "./documents";

// The browser never calls the .NET API directly: requests go through the Next.js server,
// which adds the tenant header (and, later, the user's access token).
const API_URL = process.env.DOCUMIND_API_URL ?? "http://localhost:5288";
const TENANT_ID = process.env.DOCUMIND_TENANT_ID ?? "";

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
  ) {
    super(message);
  }
}

export function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers);
  if (TENANT_ID) {
    headers.set("X-Tenant-Id", TENANT_ID);
  }

  return fetch(`${API_URL}${path}`, { ...init, headers, cache: "no-store" });
}

// Turns an ASP.NET ProblemDetails response into a readable error
async function toApiError(response: Response): Promise<ApiError> {
  let message = `Request failed with status ${response.status}.`;
  try {
    const problem = await response.json();
    message = problem.detail ?? problem.title ?? message;
  } catch {
    // Body was not JSON; keep the generic message
  }

  return new ApiError(message, response.status);
}

export async function listDocuments(): Promise<DocumentSummary[]> {
  const response = await apiFetch("/api/documents");
  if (!response.ok) throw await toApiError(response);
  return response.json();
}

export async function uploadDocument(file: File, title: string): Promise<DocumentSummary> {
  const body = new FormData();
  body.append("file", file, file.name);
  if (title) body.append("title", title);

  const response = await apiFetch("/api/documents", { method: "POST", body });
  if (!response.ok) throw await toApiError(response);
  return response.json();
}

export async function deleteDocument(id: string): Promise<void> {
  const response = await apiFetch(`/api/documents/${encodeURIComponent(id)}`, { method: "DELETE" });
  if (!response.ok && response.status !== 404) throw await toApiError(response);
}
