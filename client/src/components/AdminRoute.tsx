import { Navigate, Outlet } from "react-router-dom";
import { useSession } from "../lib/authClient.js";
import { Skeleton } from "./ui/skeleton.js";

export function AdminRoute() {
  const { data: session, isPending } = useSession();
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
  // "Admin" mirrors server-side UserRole.Admin enum value
  if (!session || session.user.role !== "Admin") return <Navigate to="/dashboard" replace />;
  return <Outlet />;
}
