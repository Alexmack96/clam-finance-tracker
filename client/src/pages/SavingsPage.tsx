import { useQuery } from "@tanstack/react-query";
import { Info } from "lucide-react";
import type { Bucket } from "@clam/core";
import api from "../lib/api.js";
import { findLatestSalary } from "../lib/salary.js";
import { aggregateMonthlySavings } from "../lib/savings.js";
import { Card, CardContent, CardHeader, CardTitle } from "../components/ui/card.js";
import { Popover, PopoverContent, PopoverTrigger } from "../components/ui/popover.js";

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
  id: string;
  description: string;
  amount: string;
  date: string;
  type: "Income" | "Expense";
  owner: string;
  bucket: Bucket | null; // null = uncategorised; Savings/Ignore excluded from the score
}

interface MonthActual {
  key: string;
  label: string;
  saved: number;
  // Discretionary (Fun) spend that month — drives the in-progress month footnote.
  variable: number;
}

function useMonthlyIncome(owner: string) {
  return useQuery({
    queryKey: ["income", "salary", owner],
    enabled: !!owner,
    queryFn: async () => {
      const { data } = await api.get<Tx[]>("/api/transactions", {
        params: { type: "Income", owner },
      });
      return findLatestSalary(data);
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

      // Owner weighting (self ×1, Joint ×½) is applied inside the aggregator by `owner`.
      const agg = aggregateMonthlySavings([...ownerTxs, ...jointTxs], owner);

      const months: MonthActual[] = Object.keys(agg)
        .filter((k) => k >= "2025-01")
        .sort()
        .map((key) => {
          const [yr, mo] = key.split("-");
          const label = new Date(parseInt(yr), parseInt(mo) - 1, 1).toLocaleDateString("en-GB", {
            month: "short",
            year: "numeric",
          });
          return { key, label, saved: agg[key].saved, variable: agg[key].variable };
        });

      return months;
    },
  });
}

function scoreTheme(score: number | null) {
  if (score === null)
    return {
      hex: "#6b7280",
      text: "text-muted-foreground",
      badge: "text-muted-foreground border-border",
      label: "No data yet",
    };
  if (score >= 80)
    return {
      hex: "#22c55e",
      text: "text-emerald-500",
      badge: "text-emerald-500 border-emerald-500/40 bg-emerald-500/10",
      label: "On track",
    };
  if (score >= 50)
    return {
      hex: "#f59e0b",
      text: "text-amber-500",
      badge: "text-amber-500 border-amber-500/40 bg-amber-500/10",
      label: "Getting there",
    };
  return {
    hex: "#ef4444",
    text: "text-red-500",
    badge: "text-red-500 border-red-500/40 bg-red-500/10",
    label: "Below target",
  };
}

function ScoreRing({
  score,
  size = 112,
  stroke = 10,
  loading = false,
}: {
  score: number | null;
  size?: number;
  stroke?: number;
  loading?: boolean;
}) {
  const { hex, text } = scoreTheme(loading ? null : score);
  const r = (size - stroke) / 2;
  const circ = 2 * Math.PI * r;
  const filled = Math.min(Math.max(score ?? 0, 0), 100);
  return (
    <div className="relative shrink-0" style={{ width: size, height: size }}>
      <svg
        viewBox={`0 0 ${size} ${size}`}
        className="w-full h-full"
        style={{ transform: "rotate(-90deg)" }}
      >
        <circle
          cx={size / 2}
          cy={size / 2}
          r={r}
          fill="none"
          stroke="oklch(0.27 0.05 290)"
          strokeWidth={stroke}
        />
        <circle
          cx={size / 2}
          cy={size / 2}
          r={r}
          fill="none"
          stroke={hex}
          strokeWidth={stroke}
          strokeLinecap="round"
          strokeDasharray={circ}
          strokeDashoffset={loading ? circ : circ * (1 - filled / 100)}
          style={{
            transition: "stroke-dashoffset 0.6s cubic-bezier(0.2,0.8,0.2,1), stroke 0.4s ease",
          }}
        />
      </svg>
      <div className="absolute inset-0 flex flex-col items-center justify-center gap-0.5">
        <span className="eyebrow" style={{ fontSize: size * 0.075, letterSpacing: "0.18em" }}>
          SCORE
        </span>
        <span
          className={`font-display font-light leading-none ${
            loading ? "animate-pulse text-muted-foreground" : text
          }`}
          style={{ fontSize: size * 0.32 }}
        >
          {!loading && score !== null ? score : "—"}
        </span>
        <span className="text-muted-foreground" style={{ fontSize: size * 0.095 }}>
          /&thinsp;100
        </span>
      </div>
    </div>
  );
}

export function SavingsPage() {
  // The savings score is Alex's: all of Alex + half of Joint, against 20% of Alex's income.
  const owner = "Alex";

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
  const currentMonthName = now.toLocaleDateString("en-GB", { month: "long" });
  // The score reflects the last *complete* month (e.g. May while you're in June).
  const prevDate = new Date(now.getFullYear(), now.getMonth() - 1, 1);
  const scoreMonthKey = `${prevDate.getFullYear()}-${String(prevDate.getMonth() + 1).padStart(2, "0")}`;
  const scoreMonthName = prevDate.toLocaleDateString("en-GB", { month: "long", year: "numeric" });

  const scoreActual = actuals?.find((m) => m.key === scoreMonthKey);
  const scoreSaved = scoreActual?.saved ?? null;
  const currentVariable = actuals?.find((m) => m.key === currentMonthKey)?.variable ?? 0;

  // Convert saved-vs-target into a 0–100 score. Break-even (saved £0) sits at 20, hitting the
  // 20%-of-income target is 100, scaled linearly and clamped — so a small miss reads ~20, not 0.
  const toScore = (saved: number | null) =>
    SAVING <= 0 || saved === null
      ? null
      : Math.max(0, Math.min(Math.round(20 + 80 * (saved / SAVING)), 100));
  const score = toScore(scoreSaved);
  const scoreLoading = actualsLoading || incomeLoading;
  const theme = scoreTheme(score);

  // Year-to-date history, most recent first, excluding the in-progress month.
  const history = (actuals ?? [])
    .filter((m) => m.key.startsWith(`${now.getFullYear()}-`) && m.key !== currentMonthKey)
    .slice()
    .reverse();

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
          <ScoreRing score={score} size={112} loading={scoreLoading} />
          <span className="text-[9px] text-muted-foreground -mt-0.5">{scoreMonthName}</span>
          <div className="flex items-center gap-1">
            <span className={`text-[10px] font-semibold ${theme.text}`}>{theme.label}</span>
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
                  Your score (out of 100) reflects last complete month — how close you got to saving
                  20% of your take-home income. Hitting or beating the target scores 100.
                </p>
                <p className="text-muted-foreground">
                  Tip: set one-off or mandatory items (a wedding ring, an investment transfer) to
                  the Ignore bucket on the transactions table — they won't count against your score.
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
                <p className="eyebrow">Last month's savings score</p>
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
                      Your score (out of 100) reflects last complete month — how close you got to
                      saving 20% of your take-home income. Hitting or beating the target scores 100.
                    </p>
                    <p className="text-muted-foreground">
                      Tip: set one-off or mandatory items (a wedding ring, an investment transfer)
                      to the Ignore bucket on the transactions table — they won't count against your
                      score. Unplanned overspend stays in scope.
                    </p>
                  </PopoverContent>
                </Popover>
              </div>
              <p className="text-xs text-muted-foreground">{scoreMonthName}</p>
            </div>
            <span
              className={`text-xs font-semibold px-2.5 py-1 rounded-full border ${theme.badge}`}
            >
              {theme.label}
            </span>
          </div>
          <div className="flex items-center gap-6">
            <ScoreRing score={score} size={148} stroke={12} loading={scoreLoading} />
            <div className="flex flex-col gap-1">
              <span className="font-display text-[32px] leading-none font-light text-foreground">
                {scoreSaved !== null ? fmtExact(scoreSaved) : "—"}
              </span>
              <span className="text-sm text-muted-foreground">
                saved of {SAVING > 0 ? fmt(SAVING) : "—"} target
              </span>
              <span className="text-xs text-muted-foreground mt-1">
                Score = how close you are to your 20% target. 80+ green · 50–79 amber · under 50
                red.
              </span>
            </div>
          </div>
          <p className="text-xs text-muted-foreground mt-4 pt-3 border-t border-border">
            {currentMonthName} so far —{" "}
            <span className="font-medium text-foreground">{fmt(currentVariable)}</span> spent
            outside of fixed
          </p>
        </CardContent>
      </Card>

      {/* Savings target + monthly score history */}
      <Card className="rise rise-3 bg-emerald-500/10 border-emerald-500/30">
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
            <div className="flex items-center justify-between">
              <p className="text-xs uppercase tracking-widest text-muted-foreground">
                {owner} actual + ½ Joint · {now.getFullYear()}
              </p>
              <p className="text-[10px] uppercase tracking-widest text-muted-foreground/70">
                Score · Saved
              </p>
            </div>
            {actualsLoading ? (
              <p className="text-sm text-muted-foreground animate-pulse">Loading…</p>
            ) : !history.length ? (
              <p className="text-sm text-muted-foreground">No data yet</p>
            ) : (
              <div className="space-y-1.5">
                {history.map((m) => {
                  const s = toScore(m.saved);
                  const t = scoreTheme(s);
                  const positive = m.saved >= 0;
                  return (
                    <div key={m.key} className="flex items-center gap-3">
                      <span className="text-xs text-muted-foreground w-16 shrink-0">{m.label}</span>
                      <div className="flex-1 h-2 rounded-full bg-muted overflow-hidden">
                        <div
                          className="h-full rounded-full transition-all"
                          style={{ width: `${s ?? 0}%`, backgroundColor: t.hex }}
                        />
                      </div>
                      <span
                        className="text-xs font-semibold w-8 text-right shrink-0"
                        style={{ color: t.hex }}
                      >
                        {s ?? "—"}
                      </span>
                      <span
                        className={`text-xs font-semibold w-20 text-right shrink-0 ${
                          positive ? "text-emerald-500" : "text-red-500"
                        }`}
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
    </div>
  );
}
