import { useState } from "react";
import { NavLink, useLocation } from "react-router-dom";
import {
  Home,
  BarChart3,
  PiggyBank,
  TrendingUp,
  MoreHorizontal,
  Receipt,
  ListTodo,
  Users,
  Upload,
} from "lucide-react";
import { Popover, PopoverContent, PopoverTrigger } from "./ui/popover.js";
import { useSession } from "../lib/authClient.js";

const mainTabs = [
  { to: "/dashboard",   label: "Home",        Icon: Home },
  { to: "/analytics",   label: "Analytics",   Icon: BarChart3 },
  { to: "/savings",     label: "Savings",     Icon: PiggyBank },
  { to: "/investments", label: "Investments", Icon: TrendingUp },
] as const;

const otherItems = [
  { to: "/tabs",  label: "Tabs",  Icon: Receipt },
  { to: "/tasks", label: "Tasks", Icon: ListTodo },
];

const adminItems = [
  { to: "/users",  label: "Users",  Icon: Users },
  { to: "/import", label: "Import", Icon: Upload },
];

const otherRoutes = ["/tabs", "/tasks", "/users", "/import"];

export function BottomNav() {
  const [open, setOpen] = useState(false);
  const { data: session } = useSession();
  const { pathname } = useLocation();
  const otherActive = otherRoutes.some((r) => pathname.startsWith(r));

  const extraItems = [
    ...otherItems,
    ...(session?.user.role === "Admin" ? adminItems : []),
  ];

  return (
    <nav
      className="md:hidden shrink-0 z-40 glass-pane border-t border-border app-chrome"
      style={{ paddingBottom: "env(safe-area-inset-bottom)" }}
    >
      <div className="flex h-14">
        {mainTabs.map(({ to, label, Icon }) => (
          <NavLink
            key={to}
            to={to}
            end={to === "/dashboard"}
            className={({ isActive }) =>
              `flex-1 flex flex-col items-center justify-center gap-0.5 transition-all active:scale-[0.92] active:opacity-70 ${
                isActive ? "text-primary" : "text-muted-foreground hover:text-foreground"
              }`
            }
          >
            <Icon className="size-5" strokeWidth={1.75} />
            <span className="text-[9.5px] font-medium tracking-wide">{label}</span>
          </NavLink>
        ))}

        {/* Other — popover */}
        <Popover open={open} onOpenChange={setOpen}>
          <PopoverTrigger asChild>
            <button
              className={`flex-1 flex flex-col items-center justify-center gap-0.5 transition-all active:scale-[0.92] active:opacity-70 ${
                otherActive ? "text-primary" : "text-muted-foreground hover:text-foreground"
              }`}
            >
              <MoreHorizontal className="size-5" strokeWidth={1.75} />
              <span className="text-[9.5px] font-medium tracking-wide">Other</span>
            </button>
          </PopoverTrigger>
          <PopoverContent
            side="top"
            align="end"
            sideOffset={12}
            className="w-44 p-1.5 flex flex-col gap-0.5"
          >
            {extraItems.map(({ to, label, Icon }) => (
              <NavLink
                key={to}
                to={to}
                onClick={() => setOpen(false)}
                className={({ isActive }) =>
                  `flex items-center gap-3 px-3 py-2 rounded-md text-sm font-medium transition-colors ${
                    isActive
                      ? "bg-accent text-accent-foreground"
                      : "text-muted-foreground hover:bg-accent/60 hover:text-foreground"
                  }`
                }
              >
                <Icon className="size-4" strokeWidth={1.75} />
                {label}
              </NavLink>
            ))}
          </PopoverContent>
        </Popover>
      </div>
    </nav>
  );
}
