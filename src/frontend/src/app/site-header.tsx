"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

const links = [
  { href: "/", label: "Documents" },
  { href: "/chat", label: "Ask" },
  { href: "/team", label: "Team" },
  { href: "/settings", label: "Settings" },
];

const isAuthPage = (pathname: string) =>
  pathname === "/login" || pathname === "/signup" || pathname.startsWith("/invite/");

// userMenu is rendered on the server (it needs the session) and passed in as a slot
export function SiteHeader({ userMenu }: { userMenu: React.ReactNode }) {
  const pathname = usePathname();
  const signedInArea = !isAuthPage(pathname);

  return (
    <header className="border-b border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-900">
      <div className="mx-auto flex max-w-4xl flex-wrap items-center gap-x-6 gap-y-2 px-4 py-4 sm:px-6">
        <span className="text-lg font-semibold tracking-tight text-zinc-900 dark:text-zinc-100">DocuMind AI</span>
        {signedInArea ? (
          <>
            <nav className="flex flex-wrap gap-1">
              {links.map((link) => {
                const active = pathname === link.href;
                return (
                  <Link
                    key={link.href}
                    href={link.href}
                    className={`rounded-md px-3 py-1.5 text-sm font-medium transition-colors ${
                      active
                        ? "bg-zinc-100 text-zinc-900 dark:bg-zinc-800 dark:text-zinc-100"
                        : "text-zinc-500 hover:text-zinc-900 dark:hover:text-zinc-100"
                    }`}
                  >
                    {link.label}
                  </Link>
                );
              })}
            </nav>
            <div className="ml-auto">{userMenu}</div>
          </>
        ) : null}
      </div>
    </header>
  );
}
