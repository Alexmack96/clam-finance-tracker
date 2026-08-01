import { NavLink, Link } from "react-router-dom";
import { useSession } from "../lib/authClient.js";
import { Sun, Moon, LogOut } from "lucide-react";
import { useTheme } from "../context/theme.js";
import { Button } from "./ui/button.js";

const links = [
  { to: "/dashboard", label: "Analytics" },
  { to: "/transactions", label: "Transactions" },
  { to: "/savings", label: "Savings" },
  { to: "/investments", label: "Investments" },
  { to: "/tabs", label: "Tabs" },
  { to: "/tasks", label: "Tasks" },
  { to: "/import", label: "Import" },
  { to: "/categories", label: "Categories" },
  { to: "/rules", label: "Rules" },
  { to: "/admin", label: "Admin" },
] as const;

export function Navbar({ onSignOut }: { onSignOut: () => void }) {
  const { data: session } = useSession();
  const { theme, toggle } = useTheme();
  const initials = (session?.user.name ?? "")
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((s) => s[0]?.toUpperCase() ?? "")
    .join("");

  return (
    <header className="glass-pane shrink-0 z-40 ios-safe-top app-chrome">
      <nav className="mx-auto max-w-[1400px] px-6 h-14 flex items-center gap-8">
        {/* Wordmark */}
        <Link to="/dashboard" className="flex items-center gap-2 group shrink-0">
          <img
            src="/clam-app-logo.png"
            alt=""
            aria-hidden="true"
            className="size-8 rounded-full transition-transform duration-300 group-hover:rotate-[-6deg]"
          />
          <span className="font-display text-[19px] font-medium tracking-tight text-foreground leading-none">
            Clam<span className="text-muted-foreground/70 font-light"> Finance</span>
          </span>
        </Link>

        {/* Hairline separator */}
        <span className="hidden md:block h-5 w-px bg-border" />

        {/* Primary nav */}
        <div className="hidden md:flex items-center gap-6 text-[13px] font-medium">
          {links.map((l) => (
            <NavLink
              key={l.to}
              to={l.to}
              end={l.to === "/dashboard"}
              className={({ isActive }) => `nav-link ${isActive ? "is-active" : ""}`}
            >
              {({ isActive }) => (
                <span aria-current={isActive ? "page" : undefined}>{l.label}</span>
              )}
            </NavLink>
          ))}
        </div>

        {/* Right: identity + controls */}
        <div className="ml-auto flex items-center gap-3">
          <div className="hidden sm:flex items-center gap-2 pr-1">
            <div className="size-7 rounded-full bg-gradient-to-br from-primary to-primary/40 grid place-items-center text-[10.5px] font-semibold text-primary-foreground ring-1 ring-primary/30">
              {initials || "·"}
            </div>
            <span className="text-[12.5px] text-muted-foreground font-medium leading-none">
              {session?.user.name}
            </span>
          </div>

          <Button
            variant="ghost"
            size="icon-sm"
            onClick={toggle}
            className="rounded-full text-muted-foreground hover:text-foreground hover:bg-accent/60"
            aria-label="Toggle theme"
          >
            {theme === "dark" ? <Sun className="size-4" /> : <Moon className="size-4" />}
          </Button>

          <Button
            variant="ghost"
            size="icon-sm"
            onClick={onSignOut}
            className="rounded-full text-muted-foreground hover:text-foreground hover:bg-accent/60"
            aria-label="Sign out"
            title="Sign out"
          >
            <LogOut className="size-4" />
          </Button>
        </div>
      </nav>
    </header>
  );
}
