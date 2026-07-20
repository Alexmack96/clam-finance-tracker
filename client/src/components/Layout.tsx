import { Suspense, useEffect, useRef } from "react";
import { Outlet, useLocation, useNavigate } from "react-router-dom";
import { signOut } from "../lib/authClient.js";
import { Navbar } from "./Navbar.js";
import { BottomNav } from "./BottomNav.js";
import { Skeleton } from "./ui/skeleton.js";

function PageFallback() {
  return (
    <div className="space-y-6">
      <Skeleton className="h-8 w-48" />
      <Skeleton className="h-64 w-full" />
    </div>
  );
}

export function Layout() {
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const mainRef = useRef<HTMLElement>(null);

  useEffect(() => {
    mainRef.current?.scrollTo({ top: 0 });
  }, [pathname]);

  async function handleSignOut() {
    await signOut();
    navigate("/logged-out");
  }

  return (
    <div className="fixed inset-0 flex flex-col">
      <div className="app-atmosphere" aria-hidden />
      <Navbar onSignOut={handleSignOut} />
      <main ref={mainRef} className="flex-1 overflow-y-auto overscroll-contain px-6 py-8 md:py-12">
        <Suspense fallback={<PageFallback />}>
          <Outlet />
        </Suspense>
      </main>
      <BottomNav />
    </div>
  );
}
