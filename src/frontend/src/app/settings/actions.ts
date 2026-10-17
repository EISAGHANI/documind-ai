"use server";

import { refresh } from "next/cache";

import { ApiError, deleteApiKey, listModels, saveApiKey, setChatProvider, setModel } from "@/lib/api";
import { PROVIDER_IDS, type ProviderId } from "@/lib/settings";

export type ActionResult<T = undefined> = { ok: true; data?: T } | { ok: false; error: string };

function fail(error: unknown): { ok: false; error: string } {
  return {
    ok: false,
    error: error instanceof ApiError ? error.message : "Could not reach the DocuMind API. Is the backend running?",
  };
}

// Server Actions are public endpoints: never trust the provider value sent by the client
function isProvider(value: unknown): value is ProviderId {
  return typeof value === "string" && (PROVIDER_IDS as readonly string[]).includes(value);
}

export async function saveApiKeyAction(provider: ProviderId, apiKey: string): Promise<ActionResult<string[]>> {
  if (!isProvider(provider)) return { ok: false, error: "Unknown provider." };
  const key = String(apiKey ?? "").trim();
  if (key.length < 8 || key.length > 500) return { ok: false, error: "That doesn't look like an API key." };

  try {
    const result = await saveApiKey(provider, key);
    refresh();
    return { ok: true, data: result.models };
  } catch (error) {
    return fail(error);
  }
}

export async function listModelsAction(provider: ProviderId): Promise<ActionResult<string[]>> {
  if (!isProvider(provider)) return { ok: false, error: "Unknown provider." };
  try {
    return { ok: true, data: await listModels(provider) };
  } catch (error) {
    return fail(error);
  }
}

export async function setModelAction(provider: ProviderId, model: string): Promise<ActionResult> {
  if (!isProvider(provider)) return { ok: false, error: "Unknown provider." };
  if (typeof model !== "string" || !model || model.length > 200) return { ok: false, error: "Choose a model." };
  try {
    await setModel(provider, model);
    refresh();
    return { ok: true };
  } catch (error) {
    return fail(error);
  }
}

export async function setChatProviderAction(provider: ProviderId): Promise<ActionResult> {
  if (!isProvider(provider)) return { ok: false, error: "Unknown provider." };
  try {
    await setChatProvider(provider);
    refresh();
    return { ok: true };
  } catch (error) {
    return fail(error);
  }
}

export async function deleteApiKeyAction(provider: ProviderId): Promise<ActionResult> {
  if (!isProvider(provider)) return { ok: false, error: "Unknown provider." };
  try {
    await deleteApiKey(provider);
    refresh();
    return { ok: true };
  } catch (error) {
    return fail(error);
  }
}
