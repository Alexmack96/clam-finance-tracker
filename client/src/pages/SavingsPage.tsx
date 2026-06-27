import { useQuery } from "@tanstack/react-query";
import { Info } from "lucide-react";
import api from "../lib/api.js";
import { useSession } from "../lib/authClient.js";
import { Card, CardContent, CardHeader, CardTitle } from "../components/ui/card.js";
import { Popover, PopoverContent, PopoverTrigger } from "../components/ui/popover.js";

const SALARY_DESC = "FASTER PAYMENTS RECEIPT REF.HUDSON BAY FROM THROGMORTON UK LTD";

const fmt = (n: number) =>
  new Intl.NumberFormat("en-GB", {
    style: "currency",
    currency: "GBP",
    maximumFractionDigits: 0,
  }).format(n);

const fmtExact = (n: number) =>
  new Intl.NumberFormat("en-GB", {
    style: "currency",
    currency: "GBP",
    minimumFractionDigits: 2,
  }).format(n);

interface Tx {
  description: string;
  amount: string;
  date: string;
  type: "Income" | "Expense";
  owner: string;
}

interface MonthActual {
  key: string;
  label: string;
  saved: number;
}

function useMonthlyIncome(owner: string) {
  return useQuery({
    queryKey: ["income", "salary", owner],
    enabled: !!owner,
    queryFn: async () => {
      const { data } = await api.get<Tx[]>("/api/transactions", {
        params: { type: "Income", owner },
      });
      const salary = data
        .filter((t) => t.description === SALARY_DESC)
        .sort((a, b) => new Date(b.date).getTime() - new Date(a.date).getTime())[0];
      return salary ? parseFloat(salary.amount) : null;
    },
  });
}

function useMonthlyActuals(owner: string) {
  return useQuery({
    queryKey: ["savings", "monthly-actuals", owner],
    enabled: !!owner,
    queryFn: async () => {
      const [{ data: ownerTxs }, { data: jointTxs }] = await Promise.all([
        api.get<Tx[]>("/api/transactions", { params: { owner } }),
        api.get<Tx[]>("/api/transactions", { params: { owner: "Joint" } }),
      ]);

      const net = (txs: Tx[], weight: number) => {
        const byMonth: Record<string, number> = {};
        for (const t of txs) {
          const key = t.date.slice(0, 7);
          const sign = t.type === "Income" ? 1 : -1;
          byMonth[key] = (byMonth[key] ?? 0) + sign * parseFloat(t.amount) * weight;
        }
        return byMonth;
      };

      const ownerNet = net(ownerTxs, 1);
      const jointNet = net(jointTxs, 0.5);

      const keys = new Set([...Object.keys(ownerNet), ...Object.keys(jointNet)]);
      const months: MonthActual[] = [...keys]
        .filter((k) => k >= "2025-01")
        .sort()
        .map((key) => {
          const [yr, mo] = key.split("-");
          const label = new Date(parseInt(yr), parseInt(mo) - 1, 1).toLocaleDateString("en-GB", {
            month: "short",
            year: "numeric",
          });
          return {
            key,
            label,
            saved: (ownerNet[key] ?? 0) + (jointNet[key] ?? 0),
          };
        });

      return months;
    },
  });
}

export function SavingsPage() {
  const { data: session } = useSession();
  const owner = session?.user.name.split(" ")[0] ?? "";

  const { data: income, isLoading: incomeLoading } = useMonthlyIncome(owner);
  const { data: actuals, isLoading: actualsLoading } = useMonthlyActuals(owner);
  const INCOME = income ?? 0;
  const SAVING = INCOME * 0.2;

  const categories = [
    {
      label: "Needs",
      description: "Housing, bills, groceries, transport",
      amount: INCOME * 0.5,
      percent: "50%",
      bar: "bg-blue-500",
      text: "text-blue-500",
    },
    {
      label: "Wants",
      description: "Dining, entertainment, shopping",
      amount: INCOME * 0.3,
      percent: "30%",
      bar: "bg-amber-500",
      text: "text-amber-500",
    },
    {
      label: "Savings",
      description: "Emergency fund, ISA, pension top-up",
      amount: SAVING,
      percent: "20%",
      bar: "bg-emerald-500",
      text: "text-emerald-500",
      highlight: true,
    },
  ];

  const now = new Date();
  const currentMonthKey = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}`;
  const currentMonthActual = actuals?.find((m) => m.key === currentMonthKey);
  const currentSaved = currentMonthActual?.saved ?? null;
  const scorePercent =
    SAVING > 0 && currentSaved !== null ? Math.round((currentSaved / SAVING) * 100) : null;
  const scoreColor =
    scorePercent === null
      ? "text-muted-foreground"
      : scorePercent >= 100
        ? "text-emerald-500"
        : scorePercent >= 70
          ? "text-amber-500"
          : "text-destructive";
  const barColor =
    scorePercent === null
      ? "bg-muted-foreground"
      : scorePercent >= 100
        ? "bg-emerald-500"
        : scorePercent >= 70
          ? "bg-amber-500"
          : "bg-red-500";
  const ringHex =
    scorePercent === null
      ? "#6b7280"
      : scorePercent >= 100
        ? "#22c55e"
        : scorePercent >= 70
          ? "#f59e0b"
          : "#ef4444";
  const scoreLabel =
    scorePercent === null
      ? "No data yet"
      : scorePercent >= 100
        ? "Target hit"
        : scorePercent >= 70
          ? "Getting there"
          : "Below target";

  return (
    <div className="max-w-3xl mx-auto space-y-6">
      <header className="rise rise-1 flex items-center gap-6">
        {/* Title */}
        <div className="flex-1">
          <p className="eyebrow mb-3">
            {now.toLocaleDateString("en-GB", { month: "long", year: "numeric" })}
          </p>
          <h1 className="font-display text-[44px] leading-[1.02] font-light tracking-tight text-foreground">
            <span className="italic text-primary">Savings</span>
          </h1>
          <p className="text-sm text-muted-foreground mt-2">
            50/30/20 budget plan — track your savings score against the 20% target.
          </p>
        </div>

        {/* Score ring — mobile only, sits beside the title */}
        <div className="md:hidden shrink-0 flex flex-col items-center gap-1">
          <div className="relative size-28">
            <svg
              viewBox="0 0 120 120"
              className="w-full h-full"
              style={{ transform: "rotate(-90deg)" }}
            >
              <circle
                cx="60"
                cy="60"
                r="48"
                fill="none"
                stroke="oklch(0.27 0.05 290)"
                strokeWidth="10"
              />
              <circle
                cx="60"
                cy="60"
                r="48"
                fill="none"
                stroke={ringHex}
                strokeWidth="10"
                strokeLinecap="round"
                strokeDasharray={301.593}
                strokeDashoffset={301.593 * (1 - Math.min((scorePercent ?? 0) / 100, 1))}
                style={{
                  transition:
                    "stroke-dashoffset 0.6s cubic-bezier(0.2,0.8,0.2,1), stroke 0.4s ease",
                }}
              />
            </svg>
            <div className="absolute inset-0 flex flex-col items-center justify-center gap-0.5">
              <span className="eyebrow" style={{ fontSize: "7px", letterSpacing: "0.18em" }}>
                SCORE
              </span>
              <span className={`font-display text-[32px] leading-none font-light ${scoreColor}`}>
                {scorePercent !== null ? scorePercent : "—"}
              </span>
              <span className="text-[9px] text-muted-foreground">/&thinsp;100</span>
            </div>
          </div>
          <div className="flex items-center gap-1">
            <span className={`text-[10px] font-semibold ${scoreColor}`}>{scoreLabel}</span>
            <Popover>
              <PopoverTrigger asChild>
                <button
                  className="text-muted-foreground/50 hover:text-muted-foreground"
                  aria-label="How is this calculated?"
                >
                  <Info className="size-3" />
                </button>
              </PopoverTrigger>
              <PopoverContent side="bottom" align="end" className="w-72 text-xs space-y-2 p-4">
                <p className="font-semibold text-foreground">How the score works</p>
                <p className="text-muted-foreground">
                  Your score (out of 100) is based on how close you got to saving 20% of your
                  take-home income this month. Hitting the target exactly = 100. Going over still
                  scores 100.
                </p>
                <p className="text-muted-foreground">
                  Coming soon: mark certain spending categories — like wellbeing or holidays — as
                  allowable, so they don't count against you.
                </p>
              </PopoverContent>
            </Popover>
          </div>
        </div>
      </header>

      {/* Desktop: score card */}
      <Card className="hidden md:block rise rise-2 relative overflow-hidden">
        <CardContent className="pt-6 relative">
          <div className="flex items-start justify-between mb-4">
            <div>
              <div className="flex items-center gap-1.5 mb-1">
                <p className="eyebrow">This month's savings score</p>
                <Popover>
                  <PopoverTrigger asChild>
                    <button
                      className="text-muted-foreground/50 hover:text-muted-foreground transition-colors"
                      aria-label="How is this score calculated?"
                    >
                      <Info className="size-3" />
                    </button>
                  </PopoverTrigger>
                  <PopoverContent
                    side="bottom"
                    align="start"
                    className="w-72 text-xs space-y-2 p-4"
                  >
                    <p className="font-semibold text-foreground">How the score works</p>
                    <p className="text-muted-foreground">
                      Your score (out of 100) is based on how close you got to saving 20% of your
                      take-home income this month. Hitting the target exactly = 100. Going over
                      still scores 100.
                    </p>
                    <p className="text-muted-foreground">
                      Coming soon: mark certain spending categories — like wellbeing or holidays —
                      as allowable, so they don't count against you. Unplanned overspend will remain
                      in scope.
                    </p>
                  </PopoverContent>
                </Popover>
              </div>
              <p className="text-xs text-muted-foreground">
                {now.toLocaleDateString("en-GB", { month: "long", year: "numeric" })}
              </p>
            </div>
            <span
              className={`text-xs font-semibold px-2.5 py-1 rounded-full border ${
                scorePercent === null
                  ? "text-muted-foreground border-border"
                  : scorePercent >= 100
                    ? "text-emerald-500 border-emerald-500/40 bg-emerald-500/10"
                    : scorePercent >= 70
                      ? "text-amber-500 border-amber-500/40 bg-amber-500/10"
                      : "text-red-500 border-red-500/40 bg-red-500/10"
              }`}
            >
              {scoreLabel}
            </span>
          </div>
          <div className="flex items-baseline gap-4 mb-4">
            {actualsLoading || incomeLoading ? (
              <span className="font-display text-[56px] leading-none font-light text-muted-foreground animate-pulse">
                —%
              </span>
            ) : (
              <span className={`font-display text-[56px] leading-none font-light ${scoreColor}`}>
                {scorePercent !== null ? `${scorePercent}%` : "—%"}
              </span>
            )}
            <div className="flex flex-col">
              <span className="text-sm font-medium text-foreground">
                {currentSaved !== null ? fmtExact(currentSaved) : "—"}
              </span>
              <span className="text-xs text-muted-foreground">
                of {SAVING > 0 ? fmt(SAVING) : "—"} target
              </span>
            </div>
          </div>
          <div className="h-2 rounded-full bg-muted overflow-hidden">
            <div
              className={`h-full rounded-full transition-all ${barColor}`}
              style={{ width: `${Math.min(scorePercent ?? 0, 100)}%` }}
            />
          </div>
        </CardContent>
      </Card>

      {/* Income hero */}
      <Card className="border-2 border-primary/30 bg-primary/5">
        <CardContent className="pt-6">
          <p className="text-xs uppercase tracking-widest text-muted-foreground mb-1">
            Monthly take-home income
          </p>
          {incomeLoading ? (
            <p className="font-display text-[56px] leading-none font-light text-muted-foreground animate-pulse">
              —
            </p>
          ) : income == null ? (
            <p className="text-lg text-destructive">No salary transaction found</p>
          ) : (
            <p className="font-display text-[56px] leading-none font-light text-foreground">
              {fmtExact(income)}
            </p>
          )}
          <p className="text-xs text-muted-foreground mt-2">
            Latest Santander ({owner}) — Throgmorton UK Ltd payment
          </p>
        </CardContent>
      </Card>

      {/* 50/30/20 breakdown */}
      <div className="space-y-4">
        {categories.map((cat) => (
          <Card
            key={cat.label}
            className={cat.highlight ? "border-emerald-500/40 ring-1 ring-emerald-500/20" : ""}
          >
            <CardHeader className="pb-2">
              <div className="flex items-baseline justify-between">
                <CardTitle className={`text-sm uppercase tracking-wide ${cat.text}`}>
                  {cat.label}
                </CardTitle>
                <span className={`font-display text-[28px] leading-none font-light ${cat.text}`}>
                  {fmt(cat.amount)}
                </span>
              </div>
            </CardHeader>
            <CardContent className="space-y-2">
              <p className="text-xs text-muted-foreground">{cat.description}</p>
              <div className="flex items-center gap-3">
                <div className="flex-1 h-2 rounded-full bg-muted overflow-hidden">
                  <div
                    className={`h-full rounded-full ${cat.bar}`}
                    style={{ width: cat.percent }}
                  />
                </div>
                <span className="text-xs font-medium text-muted-foreground w-8 text-right">
                  {cat.percent}
                </span>
              </div>
            </CardContent>
          </Card>
        ))}
      </div>

      {/* Savings target + monthly actuals */}
      <Card className="bg-emerald-500/10 border-emerald-500/30">
        <CardContent className="pt-6 space-y-4">
          <div>
            <p className="text-xs uppercase tracking-widest text-muted-foreground">
              Your 20% savings target
            </p>
            <p className="font-display text-[40px] leading-none font-light text-emerald-500">
              {fmt(SAVING)}{" "}
              <span className="text-base font-normal text-muted-foreground">/ month</span>
            </p>
          </div>

          <div className="border-t border-emerald-500/20 pt-4 space-y-2">
            <p className="text-xs uppercase tracking-widest text-muted-foreground">
              {owner} actual + ½ Joint
            </p>
            {actualsLoading ? (
              <p className="text-sm text-muted-foreground animate-pulse">Loading…</p>
            ) : !actuals?.length ? (
              <p className="text-sm text-muted-foreground">No data</p>
            ) : (
              <div className="space-y-1.5">
                {actuals.map((m) => {
                  const vs = SAVING > 0 ? m.saved / SAVING : 0;
                  const positive = m.saved >= 0;
                  return (
                    <div key={m.key} className="flex items-center gap-3">
                      <span className="text-xs text-muted-foreground w-16 shrink-0">{m.label}</span>
                      <div className="flex-1 h-2 rounded-full bg-muted overflow-hidden">
                        <div
                          className={`h-full rounded-full ${positive ? "bg-emerald-500" : "bg-red-500"}`}
                          style={{ width: `${Math.min(Math.abs(vs) * 100, 100)}%` }}
                        />
                      </div>
                      <span
                        className={`text-xs font-semibold w-20 text-right shrink-0 ${positive ? "text-emerald-500" : "text-red-500"}`}
                      >
                        {fmt(m.saved)}
                      </span>
                    </div>
                  );
                })}
              </div>
            )}
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
