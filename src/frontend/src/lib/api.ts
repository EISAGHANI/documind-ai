import "server-only";

import type { CurrentUser, PendingInvitation, InvitationPreview, TeamMember, TeamRole } from "./auth-types";
import type { DocumentSummary } from "./documents";
import { getSession, type TokenResponse } from "./session";
import type { AiProviderStatus, AiSettings, ProviderId } from "./settings";

// The browser never calls the .NET API directly: requests go through the Next.js server,
// which adds the signed-in user's access token from the session cookie.
const API_URL = process.env.DOCUMIND_API_URL ?? "http://localhost:5288";

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
  ) {
    super(message);
  }
}

export async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers);
  const session = await getSession();
  if (session) {
    headers.set("Authorization", `Bearer ${session.accessToken}`);
  }

  return fetch(`${API_URL}${path}`, { ...init, headers, cache: "no-store" });
}

// For sign-in and sign-up calls, which must not carry an old session
function publicFetch(path: string, body?: unknown): Promise<Response> {
  return fetch(`${API_URL}${path}`, {
    method: body === undefined ? "GET" : "POST",
    headers: body === undefined ? undefined : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
    cache: "no-store",
  });
}

// Turns an ASP.NET ProblemDetails response into a readable error
async function toApiError(response: Response): Promise<ApiError> {
  let message =
    response.status === 401
      ? "Your session has expired. Please sign in again."
      : response.status === 403
        ? "Only company admins can do this."
        : `Request failed with status ${response.status}.`;
  try {
    const problem = await response.json();
    message = problem.detail ?? problem.title ?? message;
  } catch {
    // Body was not JSON; keep the default message
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

// --- Sign-in and team management

// Returns tokens to store in the session, or throws ApiError with the API's message
async function tokenCall(path: string, body: unknown): Promise<TokenResponse> {
  const response = await publicFetch(path, body);
  if (!response.ok) throw await toApiError(response);
  return response.json();
}

export const login = (email: string, password: string) => tokenCall("/api/auth/login", { email, password });

export const register = (companyName: string, displayName: string, email: string, password: string) =>
  tokenCall("/api/auth/register", { companyName, displayName, email, password });

export const acceptInvitation = (token: string, displayName: string, password: string) =>
  tokenCall("/api/auth/invitations/accept", { token, displayName, password });

export async function getInvitation(token: string): Promise<InvitationPreview | null> {
  const response = await publicFetch(`/api/auth/invitations/${encodeURIComponent(token)}`);
  if (response.status === 404) return null;
  if (!response.ok) throw await toApiError(response);
  return response.json();
}

// null when signed out, or when the account no longer belongs to a company
export async function getMe(): Promise<CurrentUser | null> {
  const response = await apiFetch("/api/auth/me");
  if (response.status === 401) return null;
  if (!response.ok) throw await toApiError(response);
  return response.json();
}

export async function listTeamMembers(): Promise<TeamMember[]> {
  const response = await apiFetch("/api/team/members");
  if (!response.ok) throw await toApiError(response);
  return response.json();
}

export async function listInvitations(): Promise<PendingInvitation[]> {
  const response = await apiFetch("/api/team/invitations");
  if (!response.ok) throw await toApiError(response);
  return response.json();
}

export async function createInvitation(email: string, role: TeamRole): Promise<{ invitation: PendingInvitation; token: string }> {
  const response = await sendJson("/api/team/invitations", "POST", { email, role });
  if (!response.ok) throw await toApiError(response);
  return response.json();
}

export async function revokeInvitation(id: string): Promise<void> {
  const response = await apiFetch(`/api/team/invitations/${encodeURIComponent(id)}`, { method: "DELETE" });
  if (!response.ok) throw await toApiError(response);
}

export async function changeMemberRole(userId: string, role: TeamRole): Promise<void> {
  const response = await sendJson(`/api/team/members/${encodeURIComponent(userId)}/role`, "PUT", { role });
  if (!response.ok) throw await toApiError(response);
}

export async function removeMember(userId: string): Promise<void> {
  const response = await apiFetch(`/api/team/members/${encodeURIComponent(userId)}`, { method: "DELETE" });
  if (!response.ok) throw await toApiError(response);
}
