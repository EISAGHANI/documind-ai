import { NextResponse, type NextRequest } from "next/server";

import { refreshTokens, sealSession, SESSION_COOKIE, sessionCookieOptions, unsealSession } from "@/lib/session";

// Refresh the access token when it has less than this long to live
const REFRESH_MARGIN_MS = 2 * 60 * 1000;

function isPublicPath(pathname: string): boolean {
  return pathname === "/login" || pathname === "/signup" || pathname.startsWith("/invite/");
}

// Runs before every page, Server Action and route handler. It keeps the session fresh (cookies can
// only be written here or in actions, not while a page renders) and sends signed-out visitors to
// /login. This is an optimistic check: the .NET API still verifies every request.
export async function proxy(request: NextRequest) {
  // One id per browser request, reused for every API call it triggers (see lib/trace.ts)
  const requestHeaders = new Headers(request.headers);
  if (!requestHeaders.get("x-correlation-id")) requestHeaders.set("x-correlation-id", crypto.randomUUID());
  const { pathname, search } = request.nextUrl;
  const cookie = request.cookies.get(SESSION_COOKIE)?.value;
  let session = await unsealSession(cookie);
  let refreshed: string | null = null;

  if (session && session.expiresAt - Date.now() < REFRESH_MARGIN_MS) {
    session = await refreshTokens(session.refreshToken);
    if (session) refreshed = await sealSession(session);
  }

  if (!session && !isPublicPath(pathname)) {
    const response = pathname.startsWith("/api/")
      ? NextResponse.json({ detail: "Your session has expired. Please sign in again." }, { status: 401 })
      : NextResponse.redirect(new URL(`/login?next=${encodeURIComponent(pathname + search)}`, request.url));
    if (cookie) response.cookies.delete(SESSION_COOKIE);
    return response;
  }

  if (refreshed) {
    // Give this request the new tokens, and the browser the new cookie
    request.cookies.set(SESSION_COOKIE, refreshed);
    const response = NextResponse.next({ request: { headers: requestHeaders } });
    response.cookies.set(SESSION_COOKIE, refreshed, sessionCookieOptions);
    return response;
  }

  return NextResponse.next({ request: { headers: requestHeaders } });
}

export const config = {
  // Everything except static assets
  matcher: ["/((?!_next/static|_next/image|favicon.ico|.*\\.(?:svg|png|jpg|jpeg|gif|webp|ico)$).*)"],
};
