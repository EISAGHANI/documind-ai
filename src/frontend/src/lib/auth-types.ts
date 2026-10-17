// Shared between server and client code

export type TeamRole = "Admin" | "Member";

export type CurrentUser = {
  userId: string;
  email: string;
  displayName: string;
  tenantId: string;
  tenantName: string;
  role: TeamRole;
};

export type TeamMember = {
  userId: string;
  email: string;
  displayName: string;
  role: TeamRole;
  joinedAt: string;
};

export type PendingInvitation = {
  id: string;
  email: string;
  role: TeamRole;
  createdAt: string;
  expiresAt: string;
};

export type InvitationPreview = {
  companyName: string;
  email: string;
  role: TeamRole;
  expiresAt: string;
};

// Only same-site paths are allowed after sign-in, so a crafted ?next= can't redirect elsewhere
export function safeNextPath(value: unknown): string {
  return typeof value === "string" && value.startsWith("/") && !value.startsWith("//") && !value.startsWith("/\\")
    ? value
    : "/";
}
