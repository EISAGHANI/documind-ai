"use server";

import { redirect } from "next/navigation";

import { acceptInvitation, ApiError, login, register } from "@/lib/api";
import { safeNextPath } from "@/lib/auth-types";
import { clearSession, setSession } from "@/lib/session";

export type AuthFormState = { error?: string; values?: Record<string, string> };

const field = (formData: FormData, name: string) => String(formData.get(name) ?? "").trim();

function failure(error: unknown, values: Record<string, string>): AuthFormState {
  return {
    error: error instanceof ApiError ? error.message : "Could not reach DocuMind. Please try again.",
    // Echo non-secret fields back so the form doesn't clear on an error
    values,
  };
}

export async function loginAction(_prev: AuthFormState, formData: FormData): Promise<AuthFormState> {
  const email = field(formData, "email");
  const password = String(formData.get("password") ?? "");
  if (!email || !password) return { error: "Enter your email and password.", values: { email } };

  try {
    await setSession(await login(email, password));
  } catch (error) {
    return failure(error, { email });
  }
  redirect(safeNextPath(formData.get("next")));
}

export async function signupAction(_prev: AuthFormState, formData: FormData): Promise<AuthFormState> {
  const values = {
    companyName: field(formData, "companyName"),
    displayName: field(formData, "displayName"),
    email: field(formData, "email"),
  };
  const password = String(formData.get("password") ?? "");
  if (!values.companyName || !values.displayName || !values.email) return { error: "Fill in all fields.", values };
  if (password.length < 10) return { error: "Use a password of at least 10 characters.", values };

  try {
    await setSession(await register(values.companyName, values.displayName, values.email, password));
  } catch (error) {
    return failure(error, values);
  }
  // A new company has no API keys yet
  redirect("/settings");
}

export async function acceptInvitationAction(_prev: AuthFormState, formData: FormData): Promise<AuthFormState> {
  const token = field(formData, "token");
  const values = { displayName: field(formData, "displayName") };
  const password = String(formData.get("password") ?? "");
  if (!values.displayName) return { error: "Enter your name.", values };
  if (password.length < 10) return { error: "Use a password of at least 10 characters.", values };

  try {
    await setSession(await acceptInvitation(token, values.displayName, password));
  } catch (error) {
    return failure(error, values);
  }
  redirect("/");
}

// Bearer tokens can't be revoked server-side, so signing out removes them from the browser;
// the access token expires on its own within 30 minutes.
export async function logoutAction(): Promise<void> {
  await clearSession();
  redirect("/login");
}
