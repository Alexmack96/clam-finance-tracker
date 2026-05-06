import { Outlet, useNavigate } from "react-router-dom";
import { signOut } from "../lib/authClient.js";
import { Navbar } from "./Navbar.js";
import { BottomNav } from "./BottomNav.js";

export function Layout() {
  const navigate = useNavigate();

  async function handleSignOut() {
    await signOut();
    navigate("/logged-out");
  }

  return (
    <div className="min-h-screen relative">
      <div className="app-atmosphere" aria-hidden />
      <Navbar onSignOut={handleSignOut} />
      <main className="px-6 py-10 pb-[calc(3.5rem+env(safe-area-inset-bottom)+1.5rem)] md:py-12 md:pb-12">
        <Outlet />
      </main>
      <BottomNav />
    </div>
  );
}
