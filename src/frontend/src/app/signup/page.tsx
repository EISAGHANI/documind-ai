import type { Metadata } from "next";
import Link from "next/link";

import { signupAction } from "../auth-actions";
import { AuthCard, AuthForm } from "../auth-form";

export const metadata: Metadata = { title: "Create account · DocuMind AI" };

export default function SignupPage() {
  return (
    <AuthCard title="Create your company account" subtitle="You'll be the admin and can invite your team afterwards.">
      <AuthForm
        action={signupAction}
        submitLabel="Create account"
        fields={[
          { name: "companyName", label: "Company name", autoComplete: "organization" },
          { name: "displayName", label: "Your name", autoComplete: "name" },
          { name: "email", label: "Work email", type: "email", autoComplete: "email" },
          { name: "password", label: "Password", type: "password", autoComplete: "new-password", hint: "At least 10 characters." },
        ]}
      />
      <p className="mt-6 text-center text-sm text-zinc-500">
        Already have an account?{" "}
        <Link href="/login" className="font-medium text-indigo-600 hover:underline dark:text-indigo-400">
          Sign in
        </Link>
      </p>
    </AuthCard>
  );
}
