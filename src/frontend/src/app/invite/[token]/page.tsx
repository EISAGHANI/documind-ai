import type { Metadata } from "next";
import Link from "next/link";

import { acceptInvitationAction } from "../../auth-actions";
import { AuthCard, AuthForm } from "../../auth-form";
import { getInvitation } from "@/lib/api";

export const metadata: Metadata = { title: "Join your team · DocuMind AI" };

export default async function InvitePage(props: PageProps<"/invite/[token]">) {
  const { token } = await props.params;
  const invitation = await getInvitation(token).catch(() => null);

  if (!invitation) {
    return (
      <AuthCard title="Invitation not valid" subtitle="This link is invalid, was already used, or has expired.">
        <p className="text-sm text-zinc-500">Ask an admin of your company to send you a new invitation.</p>
        <Link href="/login" className="mt-4 inline-block text-sm font-medium text-indigo-600 hover:underline dark:text-indigo-400">
          Go to sign in
        </Link>
      </AuthCard>
    );
  }

  return (
    <AuthCard
      title={`Join ${invitation.companyName}`}
      subtitle={`You've been invited as ${invitation.role === "Admin" ? "an admin" : "a member"}. Set up your account to continue.`}
    >
      <AuthForm
        action={acceptInvitationAction}
        submitLabel="Join company"
        hidden={{ token }}
        fields={[
          { name: "email", label: "Email", type: "email", defaultValue: invitation.email, readOnly: true },
          { name: "displayName", label: "Your name", autoComplete: "name" },
          { name: "password", label: "Password", type: "password", autoComplete: "new-password", hint: "At least 10 characters." },
        ]}
      />
    </AuthCard>
  );
}
