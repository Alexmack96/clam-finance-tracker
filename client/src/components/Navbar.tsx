import { NavLink, Link } from "react-router-dom";
import { useSession } from "../lib/authClient.js";
import { Sun, Moon, LogOut } from "lucide-react";
import { useTheme } from "../context/ThemeContext.js";
import { Button } from "./ui/button.js";

const links = [
  { to: "/dashboard",   label: "Transactions" },
  { to: "/analytics",   label: "Analytics" },
  { to: "/savings",     label: "Savings" },
  { to: "/investments", label: "Investments" },
  { to: "/tabs",        label: "Tabs" },
  { to: "/tasks",       label: "Tasks" },
] as const;

const adminLinks = [
  { to: "/users",  label: "Users" },
  { to: "/import", label: "Import" },
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
    <header className="glass-pane sticky top-0 z-40 ios-safe-top">
      <nav className="mx-auto max-w-[1400px] px-6 h-14 flex items-center gap-8">
        {/* Wordmark */}
        <Link to="/dashboard" className="flex items-center gap-2.5 group shrink-0">
          <svg
            width="22" height="22" viewBox="0 0 28 28" fill="none"
            xmlns="http://www.w3.org/2000/svg" aria-hidden="true"
            className="text-primary transition-transform duration-300 group-hover:rotate-[-6deg]"
          >
            <path d="M14 3 C7 3 3 8 3 14 L25 14 C25 8 21 3 14 3 Z" fill="currentColor" opacity="0.42" />
            <path d="M14 3 L14 14"        stroke="currentColor" strokeWidth="1" opacity="0.65" />
            <path d="M9.5 4.5 L11.5 14"   stroke="currentColor" strokeWidth="1" opacity="0.65" />
            <path d="M18.5 4.5 L16.5 14"  stroke="currentColor" strokeWidth="1" opacity="0.65" />
            <path d="M5.5 8.5 L9 14"      stroke="currentColor" strokeWidth="1" opacity="0.45" />
            <path d="M22.5 8.5 L19 14"    stroke="currentColor" strokeWidth="1" opacity="0.45" />
            <path d="M3 14 C3 21 7.5 25 14 25 C20.5 25 25 21 25 14 Z" fill="currentColor" opacity="0.88" />
            <circle cx="14" cy="17" r="3.5" fill="white" opacity="0.92" />
            <circle cx="13" cy="16" r="1.2" fill="white" opacity="0.5" />
          </svg>
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
          {session?.user.role === "Admin" && (
            <>
              <span className="h-3.5 w-px bg-border" />
              {adminLinks.map((l) => (
                <NavLink
                  key={l.to}
                  to={l.to}
                  className={({ isActive }) =>
                    `nav-link text-muted-foreground/80 ${isActive ? "is-active" : ""}`
                  }
                >
                  {({ isActive }) => (
                    <span aria-current={isActive ? "page" : undefined}>{l.label}</span>
                  )}
                </NavLink>
              ))}
            </>
          )}
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
