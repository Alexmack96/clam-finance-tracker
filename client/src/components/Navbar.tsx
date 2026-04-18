import { Link } from "react-router-dom";
import { useSession } from "../lib/authClient.js";
import { Sun, Moon } from "lucide-react";
import { useTheme } from "../context/ThemeContext.js";
import { Button } from "./ui/button.js";

export function Navbar({ onSignOut }: { onSignOut: () => void }) {
  const { data: session } = useSession();
  const { theme, toggle } = useTheme();

  return (
    <nav className="bg-primary px-6 py-3 flex items-center justify-between">
      <div className="flex items-center gap-2">
        <svg width="28" height="28" viewBox="0 0 28 28" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true" className="text-primary-foreground">
          {/* Top shell */}
          <path d="M14 3 C7 3 3 8 3 14 L25 14 C25 8 21 3 14 3 Z" fill="currentColor" opacity="0.45"/>
          {/* Ridges */}
          <path d="M14 3 L14 14" stroke="currentColor" strokeWidth="1" opacity="0.65"/>
          <path d="M9.5 4.5 L11.5 14" stroke="currentColor" strokeWidth="1" opacity="0.65"/>
          <path d="M18.5 4.5 L16.5 14" stroke="currentColor" strokeWidth="1" opacity="0.65"/>
          <path d="M5.5 8.5 L9 14" stroke="currentColor" strokeWidth="1" opacity="0.45"/>
          <path d="M22.5 8.5 L19 14" stroke="currentColor" strokeWidth="1" opacity="0.45"/>
          {/* Bottom shell */}
          <path d="M3 14 C3 21 7.5 25 14 25 C20.5 25 25 21 25 14 Z" fill="currentColor" opacity="0.85"/>
          {/* Pearl */}
          <circle cx="14" cy="17" r="3.5" fill="white" opacity="0.92"/>
          <circle cx="13" cy="16" r="1.2" fill="white" opacity="0.5"/>
        </svg>
        <Link to="/dashboard" className="text-lg font-semibold text-primary-foreground hover:text-primary-foreground/80">Clam Finance Tracker</Link>
      </div>
      <div className="flex items-center gap-4">
        <Link
          to="/analytics"
          className="text-sm text-primary-foreground/80 hover:text-primary-foreground underline-offset-4 hover:underline"
        >
          Analytics
        </Link>
<Link
          to="/savings"
          className="text-sm text-primary-foreground/80 hover:text-primary-foreground underline-offset-4 hover:underline"
        >
          Savings
        </Link>
        <Link
          to="/investments"
          className="text-sm text-primary-foreground/80 hover:text-primary-foreground underline-offset-4 hover:underline"
        >
          Investments
        </Link>
        <Link
          to="/tabs"
          className="text-sm text-primary-foreground/80 hover:text-primary-foreground underline-offset-4 hover:underline"
        >
          Tabs
        </Link>
        <Link
          to="/tasks"
          className="text-sm text-primary-foreground/80 hover:text-primary-foreground underline-offset-4 hover:underline"
        >
          Tasks
        </Link>
{session?.user.role === "Admin" && (
          <>
            <Link
              to="/users"
              className="text-sm text-primary-foreground/80 hover:text-primary-foreground underline-offset-4 hover:underline"
            >
              Users
            </Link>
            <Link
              to="/import"
              className="text-sm text-primary-foreground/80 hover:text-primary-foreground underline-offset-4 hover:underline"
            >
              Import
            </Link>
          </>
        )}
        <span className="text-sm text-primary-foreground/80">{session?.user.name}</span>
        <Button
          variant="ghost"
          size="icon"
          onClick={toggle}
          className="text-primary-foreground/80 hover:text-primary-foreground hover:bg-primary-foreground/10"
          aria-label="Toggle theme"
        >
          {theme === "dark" ? <Sun /> : <Moon />}
        </Button>
        <button
          onClick={onSignOut}
          className="text-sm text-primary-foreground/80 hover:text-primary-foreground underline-offset-4 hover:underline"
        >
          Sign out
        </button>
      </div>
    </nav>
  );
}
