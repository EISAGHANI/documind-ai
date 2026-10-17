import "server-only";

import type { DocumentSummary } from "./documents";
import type { AiProviderStatus, AiSettings, ProviderId } from "./settings";

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

export async function reprocessDocument(id: string): Promise<void> {
  const response = await apiFetch(`/api/documents/${encodeURIComponent(id)}/reprocess`, { method: "POST" });
  if (!response.ok) throw await toApiError(response);
}

export async function deleteDocument(id: string): Promise<void> {
  const response = await apiFetch(`/api/documents/${encodeURIComponent(id)}`, { method: "DELETE" });
  if (!response.ok && response.status !== 404) throw await toApiError(response);
}

// --- AI provider settings (bring your own key). Keys are sent to the API once and never read back.

const settingsPath = (provider?: ProviderId, suffix = "") =>
  `/api/settings/ai${provider ? `/keys/${provider}` : ""}${suffix}`;

async function sendJson(path: string, method: string, body?: unknown): Promise<Response> {
  return apiFetch(path, {
    method,
    headers: body === undefined ? undefined : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
}

export async function getAiSettings(): Promise<AiSettings> {
  const response = await apiFetch(settingsPath());
  if (!response.ok) throw await toApiError(response);
  return response.json();
}

export async function saveApiKey(
  provider: ProviderId,
  apiKey: string,
): Promise<{ status: AiProviderStatus; models: string[] }> {
  const response = await sendJson(settingsPath(provider), "PUT", { apiKey });
  if (!response.ok) throw await toApiError(response);
  return response.json();
}

export async function listModels(provider: ProviderId): Promise<string[]> {
  const response = await apiFetch(settingsPath(provider, "/models"));
  if (!response.ok) throw await toApiError(response);
  return (await response.json()).models;
}

export async function setModel(provider: ProviderId, model: string): Promise<void> {
  const response = await sendJson(settingsPath(provider, "/model"), "PUT", { model });
  if (!response.ok) throw await toApiError(response);
}

export async function setChatProvider(provider: ProviderId): Promise<void> {
  const response = await sendJson(`${settingsPath()}/chat-provider`, "PUT", { provider });
  if (!response.ok) throw await toApiError(response);
}

export async function deleteApiKey(provider: ProviderId): Promise<void> {
  const response = await apiFetch(settingsPath(provider), { method: "DELETE" });
  if (!response.ok && response.status !== 404) throw await toApiError(response);
}
