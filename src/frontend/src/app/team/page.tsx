import type { Metadata } from "next";

import { InviteForm, InvitationRow, MemberRow } from "./team-controls";
import { listInvitations, listTeamMembers } from "@/lib/api";
import { requireUser } from "@/lib/auth";

export const metadata: Metadata = { title: "Team · DocuMind AI" };

export default async function TeamPage() {
  const user = await requireUser();
  const isAdmin = user.role === "Admin";
  const [members, invitations] = await Promise.all([listTeamMembers(), isAdmin ? listInvitations() : Promise.resolve([])]);

  return (
    <main className="mx-auto flex w-full max-w-4xl flex-col gap-6 px-4 py-8 sm:px-6">
      <div>
        <h1 className="text-xl font-semibold text-zinc-900 dark:text-zinc-100">{user.tenantName} team</h1>
        <p className="mt-1 text-sm text-zinc-500">
          Admins manage API keys and the team. Members upload documents and ask questions.
        </p>
      </div>

      {isAdmin ? (
        <section className="rounded-2xl border border-zinc-200 bg-white p-5 dark:border-zinc-800 dark:bg-zinc-900">
          <h2 className="font-semibold text-zinc-900 dark:text-zinc-100">Invite someone</h2>
          <p className="mb-4 mt-0.5 text-sm text-zinc-500">
            You&apos;ll get a link to send them. It works once and expires after 7 days.
          </p>
          <InviteForm />
        </section>
      ) : null}

      <section className="rounded-2xl border border-zinc-200 bg-white p-5 dark:border-zinc-800 dark:bg-zinc-900">
        <h2 className="font-semibold text-zinc-900 dark:text-zinc-100">Members ({members.length})</h2>
        <ul className="mt-2 divide-y divide-zinc-200 dark:divide-zinc-800">
          {members.map((member) => (
            <MemberRow key={member.userId} member={member} isSelf={member.userId === user.userId} canManage={isAdmin} />
          ))}
        </ul>
      </section>

      {isAdmin && invitations.length > 0 ? (
        <section className="rounded-2xl border border-zinc-200 bg-white p-5 dark:border-zinc-800 dark:bg-zinc-900">
          <h2 className="font-semibold text-zinc-900 dark:text-zinc-100">Pending invitations</h2>
          <ul className="mt-2 divide-y divide-zinc-200 dark:divide-zinc-800">
            {invitations.map((invitation) => (
              <InvitationRow key={invitation.id} invitation={invitation} />
            ))}
          </ul>
        </section>
      ) : null}
    </main>
  );
}
