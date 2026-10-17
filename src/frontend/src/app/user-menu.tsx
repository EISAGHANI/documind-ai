import { logoutAction } from "./auth-actions";
import { getCurrentUser } from "@/lib/auth";

// Who is signed in, for which company, plus sign-out
export async function UserMenu() {
  const user = await getCurrentUser();
  if (!user) return null;

  return (
    <div className="flex items-center gap-3 text-sm">
      <div className="text-right leading-tight">
        <div className="font-medium text-zinc-900 dark:text-zinc-100">{user.displayName}</div>
        <div className="text-xs text-zinc-500">
          {user.tenantName} · {user.role}
        </div>
      </div>
      <form action={logoutAction}>
        <button
          type="submit"
          className="rounded-md border border-zinc-300 px-2.5 py-1 text-xs font-medium text-zinc-600 hover:bg-zinc-50 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-800"
        >
          Sign out
        </button>
      </form>
    </div>
  );
}
