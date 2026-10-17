"use server";

import { refresh } from "next/cache";

import { ApiError, changeMemberRole, createInvitation, removeMember, revokeInvitation } from "@/lib/api";
import type { TeamRole } from "@/lib/auth-types";

export type TeamActionResult<T = undefined> = { ok: true; data?: T } | { ok: false; error: string };

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const isRole = (value: unknown): value is TeamRole => value === "Admin" || value === "Member";

function fail(error: unknown): { ok: false; error: string } {
  return { ok: false, error: error instanceof ApiError ? error.message : "Could not reach DocuMind. Please try again." };
}

// Admin-only actions; the API enforces the role, these only validate input shape

export async function inviteAction(email: string, role: TeamRole): Promise<TeamActionResult<string>> {
  const address = String(email ?? "").trim();
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(address) || address.length > 256) return { ok: false, error: "Enter a valid email." };
  if (!isRole(role)) return { ok: false, error: "Choose a role." };
  try {
    const { token } = await createInvitation(address, role);
    refresh();
    return { ok: true, data: token };
  } catch (error) {
    return fail(error);
  }
}

export async function revokeInvitationAction(id: string): Promise<TeamActionResult> {
  if (!GUID_PATTERN.test(String(id))) return { ok: false, error: "Invalid invitation." };
  try {
    await revokeInvitation(id);
    refresh();
    return { ok: true };
  } catch (error) {
    return fail(error);
  }
}

export async function changeRoleAction(userId: string, role: TeamRole): Promise<TeamActionResult> {
  if (!GUID_PATTERN.test(String(userId)) || !isRole(role)) return { ok: false, error: "Invalid request." };
  try {
    await changeMemberRole(userId, role);
    refresh();
    return { ok: true };
  } catch (error) {
    return fail(error);
  }
}

export async function removeMemberAction(userId: string): Promise<TeamActionResult> {
  if (!GUID_PATTERN.test(String(userId))) return { ok: false, error: "Invalid request." };
  try {
    await removeMember(userId);
    refresh();
    return { ok: true };
  } catch (error) {
    return fail(error);
  }
}
