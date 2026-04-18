import { useQuery } from "@tanstack/react-query";
import api from "../lib/api.js";
import { useSession } from "../lib/authClient.js";
import { Card, CardContent, CardHeader, CardTitle } from "../components/ui/card.js";

const SALARY_DESC = "FASTER PAYMENTS RECEIPT REF.HUDSON BAY FROM THROGMORTON UK LTD";

const fmt = (n: number) =>
  new Intl.NumberFormat("en-GB", { style: "currency", currency: "GBP", maximumFractionDigits: 0 }).format(n);

const fmtExact = (n: number) =>
  new Intl.NumberFormat("en-GB", { style: "currency", currency: "GBP", minimumFractionDigits: 2 }).format(n);

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
          const label = new Date(parseInt(yr), parseInt(mo) - 1, 1)
            .toLocaleDateString("en-GB", { month: "short", year: "numeric" });
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
  const SAVING = INCOME * 0.20;

  const categories = [
    {
      label: "Needs",
      description: "Housing, bills, groceries, transport",
      amount: INCOME * 0.50,
      percent: "50%",
      bar: "bg-blue-500",
      text: "text-blue-500",
    },
    {
      label: "Wants",
      description: "Dining, entertainment, shopping",
      amount: INCOME * 0.30,
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

  return (
    <div className="max-w-3xl mx-auto space-y-6">
      <div>
        <h1 className="text-3xl font-bold text-foreground">Savings &amp; Investments</h1>
        <p className="text-sm text-muted-foreground uppercase tracking-wide mt-1">50 / 30 / 20 budget plan</p>
      </div>

      {/* Income hero */}
      <Card className="border-2 border-primary/30 bg-primary/5">
        <CardContent className="pt-6">
          <p className="text-xs uppercase tracking-widest text-muted-foreground mb-1">Monthly take-home income</p>
          {incomeLoading ? (
            <p className="text-5xl font-extrabold text-muted-foreground animate-pulse">Loading…</p>
          ) : income == null ? (
            <p className="text-lg text-destructive">No salary transaction found</p>
          ) : (
            <p className="text-5xl font-extrabold text-foreground">{fmtExact(income)}</p>
          )}
          <p className="text-xs text-muted-foreground mt-2">Latest Santander ({owner}) — Throgmorton UK Ltd payment</p>
        </CardContent>
      </Card>

      {/* 50/30/20 breakdown */}
      <div className="space-y-4">
        {categories.map((cat) => (
          <Card key={cat.label} className={cat.highlight ? "border-emerald-500/40 ring-1 ring-emerald-500/20" : ""}>
            <CardHeader className="pb-2">
              <div className="flex items-baseline justify-between">
                <CardTitle className={`text-sm uppercase tracking-wide ${cat.text}`}>{cat.label}</CardTitle>
                <span className={`text-2xl font-bold ${cat.text}`}>{fmt(cat.amount)}</span>
              </div>
            </CardHeader>
            <CardContent className="space-y-2">
              <p className="text-xs text-muted-foreground">{cat.description}</p>
              <div className="flex items-center gap-3">
                <div className="flex-1 h-2 rounded-full bg-muted overflow-hidden">
                  <div className={`h-full rounded-full ${cat.bar}`} style={{ width: cat.percent }} />
                </div>
                <span className="text-xs font-medium text-muted-foreground w-8 text-right">{cat.percent}</span>
              </div>
            </CardContent>
          </Card>
        ))}
      </div>

      {/* Savings target + monthly actuals */}
      <Card className="bg-emerald-500/10 border-emerald-500/30">
        <CardContent className="pt-6 space-y-4">
          <div>
            <p className="text-xs uppercase tracking-widest text-muted-foreground">Your 20% savings target</p>
            <p className="text-4xl font-extrabold text-emerald-500">
              {fmt(SAVING)} <span className="text-base font-normal text-muted-foreground">/ month</span>
            </p>
          </div>

          <div className="border-t border-emerald-500/20 pt-4 space-y-2">
            <p className="text-xs uppercase tracking-widest text-muted-foreground">{owner} actual + ½ Joint</p>
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
                      <span className={`text-xs font-semibold w-20 text-right shrink-0 ${positive ? "text-emerald-500" : "text-red-500"}`}>
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
