import "server-only";

import { redirect } from "next/navigation";
import { cache } from "react";

import { getMe } from "./api";
import type { CurrentUser } from "./auth-types";
import { getSession } from "./session";

// Data access layer for the signed-in user. Pages call these instead of trusting the proxy alone;
// cache() makes repeated calls within one render share a single API request.
export const getCurrentUser = cache(async (): Promise<CurrentUser | null> => {
  if (!(await getSession())) return null;
  try {
    return await getMe();
  } catch {
    return null;
  }
});

export async function requireUser(): Promise<CurrentUser> {
  const user = await getCurrentUser();
  if (!user) redirect("/login");
  return user;
}

export async function requireAdmin(): Promise<CurrentUser> {
  const user = await requireUser();
  if (user.role !== "Admin") redirect("/");
  return user;
}
