import "server-only";

import { cookies } from "next/headers";

// The browser holds the API's access and refresh tokens only inside this encrypted, HttpOnly
// cookie: page scripts can't read it, and without SESSION_SECRET its contents are unreadable.

export const SESSION_COOKIE = "documind_session";

// Matches the API's refresh token lifetime; the access token inside is refreshed by proxy.ts
const SESSION_MAX_AGE_SECONDS = 14 * 24 * 60 * 60;

export type Session = {
  accessToken: string;
  refreshToken: string;
  // When the access token expires (ms since epoch)
  expiresAt: number;
};

// Shape of ASP.NET Core Identity's bearer token response
export type TokenResponse = {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
};

export const sessionCookieOptions = {
  httpOnly: true,
  secure: process.env.NODE_ENV === "production",
  sameSite: "lax" as const,
  path: "/",
  maxAge: SESSION_MAX_AGE_SECONDS,
};

let keyPromise: Promise<CryptoKey> | null = null;

function getKey(): Promise<CryptoKey> {
  const secret = process.env.SESSION_SECRET;
  if (!secret || secret.length < 32) {
    throw new Error("SESSION_SECRET must be set to a random value of at least 32 characters.");
  }
  // Derive a fixed-length AES key from the secret
  keyPromise ??= crypto.subtle
    .digest("SHA-256", new TextEncoder().encode(secret))
    .then((hash) => crypto.subtle.importKey("raw", hash, "AES-GCM", false, ["encrypt", "decrypt"]));
  return keyPromise;
}

function toBase64Url(bytes: Uint8Array): string {
  return Buffer.from(bytes).toString("base64url");
}

export function sessionFromTokens(tokens: TokenResponse): Session {
  return {
    accessToken: tokens.accessToken,
    refreshToken: tokens.refreshToken,
    expiresAt: Date.now() + tokens.expiresIn * 1000,
  };
}

// AES-GCM: encrypts and authenticates, so a tampered cookie fails to open
export async function sealSession(session: Session): Promise<string> {
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const plaintext = new TextEncoder().encode(JSON.stringify(session));
  const ciphertext = new Uint8Array(await crypto.subtle.encrypt({ name: "AES-GCM", iv }, await getKey(), plaintext));
  return `${toBase64Url(iv)}.${toBase64Url(ciphertext)}`;
}

export async function unsealSession(value: string | undefined): Promise<Session | null> {
  if (!value) return null;
  const [iv, ciphertext] = value.split(".");
  if (!iv || !ciphertext) return null;
  try {
    const plaintext = await crypto.subtle.decrypt(
      { name: "AES-GCM", iv: Buffer.from(iv, "base64url") },
      await getKey(),
      Buffer.from(ciphertext, "base64url"),
    );
    const session = JSON.parse(new TextDecoder().decode(plaintext)) as Session;
    return session.accessToken && session.refreshToken ? session : null;
  } catch {
    // Tampered, or sealed with a different secret
    return null;
  }
}

// Exchanges a refresh token for new tokens; null when the session can't be renewed
export async function refreshTokens(refreshToken: string): Promise<Session | null> {
  try {
    const response = await fetch(`${process.env.DOCUMIND_API_URL ?? "http://localhost:5288"}/api/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken }),
      cache: "no-store",
    });
    return response.ok ? sessionFromTokens(await response.json()) : null;
  } catch {
    return null;
  }
}

export async function getSession(): Promise<Session | null> {
  return unsealSession((await cookies()).get(SESSION_COOKIE)?.value);
}

// Only callable from Server Actions and Route Handlers (where cookies can be written)
export async function setSession(tokens: TokenResponse): Promise<void> {
  (await cookies()).set(SESSION_COOKIE, await sealSession(sessionFromTokens(tokens)), sessionCookieOptions);
}

export async function clearSession(): Promise<void> {
  (await cookies()).delete(SESSION_COOKIE);
}
