import { useAuth } from "@workos-inc/authkit-react";
import { useQuery } from "@tanstack/react-query";
import api from "./api.js";

export interface SessionUser {
  id: string;
  email: string;
  name: string;
  owner: "Alex" | "Casey" | "Joint" | null;
}

export interface Session {
  user: SessionUser;
}

/// WorkOS says *who* signed in. This says which of Alex and Casey they are, and
/// only the API knows that: a WorkOS access token carries `sub` and no profile
/// at all, so `owner` and `name` come from GET /api/me.
///
/// It keeps Better Auth's `{ data, isPending }` shape on purpose. Four pages read
/// `session.user.owner` to default their person dropdown and the navbar reads
/// `session.user.name`; none of them needs to know that identity moved.
export function useSession(): { data: Session | null; isPending: boolean } {
  const { user, isLoading } = useAuth();

  const { data, isPending, isError } = useQuery({
    queryKey: ["me", user?.id],
    queryFn: () => api.get<Session>("/api/me").then((r) => r.data),
    // No WorkOS user means no token to send, so /api/me would only 401.
    enabled: Boolean(user),
    // A 404 here means "signed in, no local row yet", which ProvisionOnFirstSignIn
    // fixes and then invalidates. Retrying would race it.
    retry: false,
  });

  if (isLoading || (user && isPending && !isError)) {
    return { data: null, isPending: true };
  }

  return { data: data ?? null, isPending: false };
}

export { useAuth };
