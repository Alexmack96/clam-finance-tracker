import { useEffect } from "react";
import { Navigate, Outlet } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { useAuth } from "@workos-inc/authkit-react";
import { Skeleton } from "./ui/skeleton.js";
import api from "../lib/api.js";

export function ProtectedRoute() {
  // The WorkOS session, not GET /api/me. Whether the local row exists is a
  // separate question that ProvisionOnFirstSignIn answers, and gating the router
  // on it would bounce a newly invited user back to a sign-in page they have
  // already used.
  const { user, isLoading } = useAuth();
  const queryClient = useQueryClient();

  // The app lands on Analytics after login; warm the Transactions page's data
  // in the background so it's already cached by the time the user clicks over.
  useEffect(() => {
    if (!user) return;
    queryClient.prefetchQuery({
      queryKey: ["transactions"],
      queryFn: () => api.get("/api/transactions").then((r) => r.data),
    });
    queryClient.prefetchQuery({
      queryKey: ["categories"],
      queryFn: () => api.get("/api/categories").then((r) => r.data),
    });
  }, [user, queryClient]);

  if (isLoading)
    return (
      <div className="flex flex-col items-center justify-center h-screen gap-4">
        <div className="w-full max-w-md space-y-3 px-8">
          <Skeleton className="h-8 w-3/4" />
          <Skeleton className="h-4 w-full" />
          <Skeleton className="h-4 w-5/6" />
          <Skeleton className="h-4 w-4/6" />
        </div>
      </div>
    );
  if (!user) return <Navigate to={`/login${signInFailureQuery()}`} replace />;
  return <Outlet />;
}

/// Tells "never signed in" apart from "signed in, and the token exchange failed".
///
/// WorkOS redirects back here with ?code=… and AuthKit trades it for tokens from
/// the browser. When that call fails the SDK logs to the console, sets user to
/// null and says nothing else — which lands here as an ordinary "not signed in"
/// and bounces to the login page. Pressing sign in then loops, with no error
/// anywhere on screen.
///
/// The give-away is that the code is still in the URL: on success AuthKit clears
/// it. So its presence means the round trip got all the way back and only the
/// exchange failed.
function signInFailureQuery() {
  const params = new URLSearchParams(window.location.search);

  // WorkOS's own error, e.g. a rejected redirect URI.
  if (params.has("error")) {
    return `?authError=${encodeURIComponent(params.get("error_description") ?? params.get("error")!)}`;
  }

  return params.has("code") ? "?authError=exchange" : "";
}
