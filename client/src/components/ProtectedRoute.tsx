import { useEffect } from "react";
import { Navigate, Outlet } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { useSession } from "../lib/authClient.js";
import { Skeleton } from "./ui/skeleton.js";
import api from "../lib/api.js";

export function ProtectedRoute() {
  const { data: session, isPending } = useSession();
  const queryClient = useQueryClient();

  // The app lands on Analytics after login; warm the Transactions page's data
  // in the background so it's already cached by the time the user clicks over.
  useEffect(() => {
    if (!session) return;
    queryClient.prefetchQuery({
      queryKey: ["transactions"],
      queryFn: () => api.get("/api/transactions").then((r) => r.data),
    });
    queryClient.prefetchQuery({
      queryKey: ["categories"],
      queryFn: () => api.get("/api/categories").then((r) => r.data),
    });
  }, [session, queryClient]);

  if (isPending)
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
  if (!session) return <Navigate to="/login" replace />;
  return <Outlet />;
}
