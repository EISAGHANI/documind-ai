"use client";

import { useState, useTransition } from "react";

import { changeRoleAction, inviteAction, removeMemberAction, revokeInvitationAction } from "./actions";
import type { PendingInvitation, TeamMember, TeamRole } from "@/lib/auth-types";

const inputClass =
  "rounded-lg border border-zinc-300 bg-white px-3 py-2 text-sm text-zinc-900 outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500/20 dark:border-zinc-700 dark:bg-zinc-950 dark:text-zinc-100";
const smallButton =
  "rounded-md px-2 py-1 text-sm transition-colors disabled:opacity-50 hover:bg-zinc-100 dark:hover:bg-zinc-800";

export function InviteForm() {
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<TeamRole>("Member");
  const [link, setLink] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();

  function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setLink(null);
    startTransition(async () => {
      const result = await inviteAction(email, role);
      if (!result.ok) {
        setError(result.error);
        return;
      }
      setLink(`${window.location.origin}/invite/${result.data}`);
      setCopied(false);
      setEmail("");
    });
  }

  async function copy() {
    if (!link) return;
    await navigator.clipboard.writeText(link);
    setCopied(true);
  }

  return (
    <div className="flex flex-col gap-3">
      <form onSubmit={submit} className="flex flex-col gap-2 sm:flex-row">
        <input
          type="email"
          required
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          placeholder="colleague@company.com"
          className={`flex-1 ${inputClass}`}
        />
        <select value={role} onChange={(event) => setRole(event.target.value as TeamRole)} className={inputClass}>
          <option value="Member">Member</option>
          <option value="Admin">Admin</option>
        </select>
        <button
          type="submit"
          disabled={pending || !email}
          className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-500 disabled:opacity-50"
        >
          {pending ? "Creating…" : "Create invitation"}
        </button>
      </form>

      {error ? <p className="text-sm text-red-600 dark:text-red-400">{error}</p> : null}

      {link ? (
        <div className="rounded-lg border border-emerald-200 bg-emerald-50 p-3 text-sm dark:border-emerald-900 dark:bg-emerald-950/40">
          <p className="text-emerald-800 dark:text-emerald-300">
            Invitation created. Send this link to them. It can&apos;t be shown again.
          </p>
          <div className="mt-2 flex gap-2">
            <input readOnly value={link} onFocus={(event) => event.target.select()} className={`flex-1 font-mono text-xs ${inputClass}`} />
            <button type="button" onClick={copy} className="rounded-lg border border-zinc-300 px-3 text-sm font-medium dark:border-zinc-700">
              {copied ? "Copied" : "Copy"}
            </button>
          </div>
        </div>
      ) : null}
    </div>
  );
}

export function MemberRow({ member, isSelf, canManage }: { member: TeamMember; isSelf: boolean; canManage: boolean }) {
  const [pending, startTransition] = useTransition();
  const [error, setError] = useState<string | null>(null);

  function run(task: () => Promise<{ ok: boolean; error?: string }>) {
    setError(null);
    startTransition(async () => {
      const result = await task();
      if (!result.ok) setError(result.error ?? "Something went wrong.");
    });
  }

  return (
    <li className="flex flex-col gap-2 py-3 sm:flex-row sm:items-center sm:justify-between">
      <div className="min-w-0">
        <div className="truncate font-medium text-zinc-900 dark:text-zinc-100">
          {member.displayName}
          {isSelf ? <span className="ml-1 text-xs font-normal text-zinc-500">(you)</span> : null}
        </div>
        <div className="truncate text-sm text-zinc-500">{member.email}</div>
        {error ? <div className="text-sm text-red-600 dark:text-red-400">{error}</div> : null}
      </div>
      <div className="flex items-center gap-2">
        {canManage && !isSelf ? (
          <>
            <select
              value={member.role}
              disabled={pending}
              onChange={(event) => run(() => changeRoleAction(member.userId, event.target.value as TeamRole))}
              className="rounded-md border border-zinc-300 bg-white px-2 py-1 text-sm dark:border-zinc-700 dark:bg-zinc-950"
            >
              <option value="Member">Member</option>
              <option value="Admin">Admin</option>
            </select>
            <button
              type="button"
              disabled={pending}
              onClick={() => {
                if (window.confirm(`Remove ${member.displayName}? Their account will be deleted.`)) {
                  run(() => removeMemberAction(member.userId));
                }
              }}
              className={`${smallButton} text-red-600 dark:text-red-400`}
            >
              Remove
            </button>
          </>
        ) : (
          <span className="rounded-full bg-zinc-100 px-2.5 py-0.5 text-xs font-medium text-zinc-700 dark:bg-zinc-800 dark:text-zinc-300">
            {member.role}
          </span>
        )}
      </div>
    </li>
  );
}

export function InvitationRow({ invitation }: { invitation: PendingInvitation }) {
  const [pending, startTransition] = useTransition();

  return (
    <li className="flex items-center justify-between gap-3 py-3">
      <div className="min-w-0">
        <div className="truncate text-sm font-medium text-zinc-900 dark:text-zinc-100">{invitation.email}</div>
        <div className="text-xs text-zinc-500">
          {invitation.role} · expires {new Date(invitation.expiresAt).toLocaleDateString()}
        </div>
      </div>
      <button
        type="button"
        disabled={pending}
        onClick={() => startTransition(async () => void (await revokeInvitationAction(invitation.id)))}
        className={`${smallButton} text-zinc-600 dark:text-zinc-300`}
      >
        {pending ? "Revoking…" : "Revoke"}
      </button>
    </li>
  );
}
