import { useState } from "react";
import { CheckCircle2, Circle } from "lucide-react";
import { Card, CardContent } from "../components/ui/card.js";

// `note` and `highImpact` are optional — most tasks have neither. Without the
// explicit type they'd be inferred away, and the note styling below can't compile.
type Task = { id: string; label: string; note?: string; highImpact?: boolean };

const TASKS: Task[] = [
  { id: "return-packages", label: "Return unwanted packages" },
  { id: "pay-debts", label: "Pay off debts" },
  { id: "categorize", label: "Categorize for the month" },
  { id: "track-aaw", label: "Track AAW progress", note: "£175k Net worth goal" },
  { id: "discount-code", label: "Use a discount code" },
  { id: "automate-savings", label: "Automate savings/investments" },
  { id: "audit-subs", label: "Audit subscriptions" },
];

function monthKey() {
  const d = new Date();
  return `tasks-${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
}

function load(): Set<string> {
  try {
    const raw = localStorage.getItem(monthKey());
    if (raw) return new Set(JSON.parse(raw) as string[]);
  } catch {
    /* ignore */
  }
  return new Set();
}

function save(checked: Set<string>) {
  localStorage.setItem(monthKey(), JSON.stringify([...checked]));
}

export function TasksPage() {
  const [checked, setChecked] = useState<Set<string>>(load);

  function toggle(id: string) {
    setChecked((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      save(next);
      return next;
    });
  }

  const now = new Date();
  const monthLabel = now.toLocaleString("en-GB", { month: "long", year: "numeric" });
  const done = TASKS.filter((t) => checked.has(t.id)).length;

  return (
    <div className="max-w-2xl mx-auto space-y-6">
      <div>
        <h1 className="text-3xl font-bold text-foreground">Tasks</h1>
        <p className="text-sm text-muted-foreground uppercase tracking-wide mt-1">
          {monthLabel} · {done}/{TASKS.length} done
        </p>
      </div>

      <Card>
        <CardContent className="py-2 divide-y divide-border">
          {TASKS.map((task) => {
            const isDone = checked.has(task.id);
            return (
              <button
                key={task.id}
                onClick={() => toggle(task.id)}
                className="w-full flex items-start gap-3 py-3 px-1 text-left hover:bg-muted/40 rounded transition-colors"
              >
                {isDone ? (
                  <CheckCircle2 className="h-5 w-5 text-primary mt-0.5 shrink-0" />
                ) : (
                  <Circle className="h-5 w-5 text-muted-foreground mt-0.5 shrink-0" />
                )}
                <div>
                  <span
                    className={`text-sm font-medium ${isDone ? "line-through text-muted-foreground" : "text-foreground"}`}
                  >
                    {task.label}
                  </span>
                  {task.note && (
                    <p
                      className={`text-xs mt-0.5 ${task.highImpact ? "text-primary/80 font-medium" : "text-muted-foreground"}`}
                    >
                      {task.note}
                    </p>
                  )}
                </div>
              </button>
            );
          })}
        </CardContent>
      </Card>
    </div>
  );
}
