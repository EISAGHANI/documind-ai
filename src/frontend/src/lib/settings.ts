// Shared types for AI provider (bring-your-own-key) settings

export type ProviderId = "anthropic" | "openai" | "gemini" | "voyage";

export type AiProviderStatus = {
  provider: ProviderId;
  displayName: string;
  purpose: "chat" | "embeddings";
  configured: boolean;
  keyHint: string | null;
  model: string | null;
  updatedAt: string | null;
};

export type AiSettings = {
  chatProvider: ProviderId | null;
  providers: AiProviderStatus[];
};

export const PROVIDER_IDS: readonly ProviderId[] = ["anthropic", "openai", "gemini", "voyage"];

// Where users create keys, shown next to each provider
export const PROVIDER_KEY_URLS: Record<ProviderId, string> = {
  anthropic: "https://platform.claude.com/settings/keys",
  openai: "https://platform.openai.com/api-keys",
  gemini: "https://aistudio.google.com/apikey",
  voyage: "https://dashboard.voyageai.com/api-keys",
};

export function isReadyForChat(settings: AiSettings): boolean {
  const voyage = settings.providers.find((p) => p.provider === "voyage");
  const chat = settings.providers.find((p) => p.provider === settings.chatProvider);
  return Boolean(voyage?.configured && chat?.configured && chat.model);
}
