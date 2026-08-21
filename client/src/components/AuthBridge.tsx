import { useEffect } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useAuth } from "@workos-inc/authkit-react";
import api, { setAccessTokenGetter } from "../lib/api.js";

/// Hands AuthKit's token getter to the axios instance.
///
/// Has to be a component: `getAccessToken` comes from a hook, so it is only
/// reachable inside the provider, and axios is a module that is imported
/// everywhere. One mount effect bridges the two.
export function AuthTokenBridge({ children }: { children: React.ReactNode }) {
  const { getAccessToken } = useAuth();

  useEffect(() => {
    setAccessTokenGetter(getAccessToken);
    return () => setAccessTokenGetter(null);
  }, [getAccessToken]);

  return <>{children}</>;
}

/// Creates the local user row the first time somebody signs in with a WorkOS
/// account this app has never seen.
///
/// The API cannot do this itself. An access token proves who is calling and
/// carries no name or email, so the browser, which has the full profile from the
/// sign-in, is the only party that can supply them.
///
/// It deliberately does not send an `owner`. Nothing here knows whether a new
/// sign-in is Alex or Casey, and guessing wrong silently points every default at
/// the wrong person's money. Rows carried over from Better Auth already have
/// theirs, and a genuinely new person gets null until somebody sets it.
export function ProvisionOnFirstSignIn() {
  const { user } = useAuth();
  const queryClient = useQueryClient();

  useEffect(() => {
    if (!user) return;

    let cancelled = false;

    async function provision() {
      try {
        await api.get("/api/me");
        return;
      } catch (error) {
        const status = (error as { response?: { status?: number } }).response?.status;
        if (status !== 404) return;
      }

      const name = [user!.firstName, user!.lastName].filter(Boolean).join(" ") || user!.email;
      await api.post("/api/users/me", { name, email: user!.email });

      if (!cancelled) await queryClient.invalidateQueries({ queryKey: ["me"] });
    }

    void provision();
    return () => {
      cancelled = true;
    };
  }, [user, queryClient]);

  return null;
}
