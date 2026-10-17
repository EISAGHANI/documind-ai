import type { Metadata } from "next";
import Link from "next/link";

import { loginAction } from "../auth-actions";
import { AuthCard, AuthForm } from "../auth-form";
import { safeNextPath } from "@/lib/auth-types";

export const metadata: Metadata = { title: "Sign in · DocuMind AI" };

export default async function LoginPage(props: PageProps<"/login">) {
  const { next } = await props.searchParams;

  return (
    <AuthCard title="Sign in" subtitle="Welcome back to DocuMind.">
      <AuthForm
        action={loginAction}
        submitLabel="Sign in"
        hidden={{ next: safeNextPath(next) }}
        fields={[
          { name: "email", label: "Email", type: "email", autoComplete: "email" },
          { name: "password", label: "Password", type: "password", autoComplete: "current-password" },
        ]}
      />
      <p className="mt-6 text-center text-sm text-zinc-500">
        New to DocuMind?{" "}
        <Link href="/signup" className="font-medium text-indigo-600 hover:underline dark:text-indigo-400">
          Create a company account
        </Link>
      </p>
    </AuthCard>
  );
}
