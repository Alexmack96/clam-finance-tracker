import { useQuery } from "@tanstack/react-query";
import {
  ComposedChart,
  Bar,
  Line,
  Cell,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
  ResponsiveContainer,
  RadialBarChart,
  RadialBar,
  PolarAngleAxis,
} from "recharts";
import { useState, type ComponentProps, type ReactNode } from "react";
import { Card, CardContent, CardHeader, CardTitle } from "../components/ui/card.js";
import { Skeleton } from "../components/ui/skeleton.js";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "../components/ui/select.js";
import { ChartTypeToggle, type ChartType } from "../components/ChartTypeToggle.js";
import { useSession } from "../lib/authClient.js";
import api from "../lib/api.js";

const fmt = (n: number) =>
  new Intl.NumberFormat("en-GB", {
    style: "currency",
    currency: "GBP",
    maximumFractionDigits: 0,
  }).format(n);

const TICK = "#888";
const GRID = "rgba(128,128,128,0.15)";
const PARTIAL_OPACITY = 0.35;

type Owner = "Alex" | "Casey" | "Joint";
type OwnerFilter = "All" | Owner;
const OWNER_FILTERS: OwnerFilter[] = ["All", "Alex", "Casey", "Joint"];

/// The monthly chart series. Sent unfiltered at the top level of the response
/// and again per owner under `byOwner`.
interface OwnerSeries {
  monthlyTransactionCount: { month: string; count: number; partial: boolean }[];
  monthlyFun: Record<string, number | string>[];
  monthlyVacation: { month: string; amount: number }[];
  monthlyFood: Record<string, number | string>[];
  monthlyGolf: { month: string; amount: number }[];
}

interface AnalyticsData extends OwnerSeries {
  budget: { spent: number; limit: number; month: string; day: number; daysInMonth: number };
  funCategories: { name: string; color: string }[];
  vacationColor: string;
  foodCategories: { name: string; color: string }[];
  spendingByCategory: { name: string; color: string; value: number }[];
  byOwner: Record<Owner, OwnerSeries>;
}

type TooltipFormatter = ComponentProps<typeof Tooltip>["formatter"];

function ChartSkeleton() {
  return <Skeleton className="w-full h-[280px] rounded-md" />;
}

function BudgetGauge({ budget }: { budget: AnalyticsData["budget"] }) {
  const { spent, limit, day, daysInMonth } = budget;
  const pct = limit > 0 ? (spent / limit) * 100 : 0;
  const remaining = limit - spent;
  const over = remaining < 0;

  // Green under budget, amber as it fills, red once over.
  const color = over ? "#ef4444" : pct >= 80 ? "#f59e0b" : "var(--primary)";

  return (
    <div className="relative" style={{ height: 280 }}>
      <ResponsiveContainer width="100%" height={280}>
        <RadialBarChart
          innerRadius="72%"
          outerRadius="100%"
          data={[{ value: Math.min(pct, 100) }]}
          startAngle={90}
          endAngle={-270}
        >
          <PolarAngleAxis type="number" domain={[0, 100]} angleAxisId={0} tick={false} />
          <RadialBar
            background={{ fill: "var(--muted)" }}
            dataKey="value"
            cornerRadius={20}
            fill={color}
            angleAxisId={0}
          />
        </RadialBarChart>
      </ResponsiveContainer>
      <div className="absolute inset-0 flex flex-col items-center justify-center pointer-events-none">
        <span className="text-3xl font-bold text-foreground">{fmt(spent)}</span>
        <span className="text-sm text-muted-foreground">of {fmt(limit)}</span>
        <span className="mt-1 text-sm font-medium" style={{ color }}>
          {over ? `${fmt(-remaining)} over` : `${fmt(remaining)} left`}
        </span>
        <span className="mt-2 text-xs text-muted-foreground">
          Day {day} of {daysInMonth} · {Math.round(pct)}% used
        </span>
      </div>
    </div>
  );
}

/// A chart card with its own owner filter and Line/Bar choice. Both are per card.
function ChartCard({
  title,
  description,
  data,
  isPending,
  children,
}: {
  title: string;
  description?: string;
  data: AnalyticsData | undefined;
  isPending: boolean;
  children: (type: ChartType, series: OwnerSeries) => ReactNode;
}) {
  const [type, setType] = useState<ChartType>("bar");
  const [owner, setOwner] = useState<OwnerFilter>("All");

  return (
    <Card>
      <CardHeader className="pb-2 flex-row flex-wrap items-start justify-between gap-2 space-y-0">
        <div className="space-y-1.5">
          <CardTitle className="text-xs uppercase tracking-wide text-muted-foreground">
            {title}
          </CardTitle>
          {description && <p className="text-xs text-muted-foreground">{description}</p>}
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <Select value={owner} onValueChange={(v) => setOwner(v as OwnerFilter)}>
            <SelectTrigger className="h-7 w-[90px] text-xs" aria-label={`${title} owner`}>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {OWNER_FILTERS.map((o) => (
                <SelectItem key={o} value={o}>
                  {o}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <ChartTypeToggle value={type} onChange={setType} label={`${title} chart type`} />
        </div>
      </CardHeader>
      <CardContent>
        {isPending || !data ? (
          <ChartSkeleton />
        ) : (
          children(type, owner === "All" ? data : data.byOwner[owner])
        )}
      </CardContent>
    </Card>
  );
}

interface Series {
  dataKey: string;
  name: string;
  color: string;
}

const money: { yTickFormatter: (v: number) => string; tooltipFormatter: TooltipFormatter } = {
  yTickFormatter: (v) => `£${v}`,
  tooltipFormatter: (v) => fmt(v as number),
};

/// Month-by-month series as bars or lines. Stacked series stack as bars; as
/// lines each series is drawn on its own, because stacked lines hide the trend
/// of every series above the first.
function MonthlyChart({
  type,
  data,
  series,
  stacked = false,
  allowDecimals,
  yTickFormatter,
  tooltipFormatter,
  dimPartial = false,
}: {
  type: ChartType;
  data: Record<string, unknown>[];
  series: Series[];
  stacked?: boolean;
  allowDecimals?: boolean;
  yTickFormatter?: (v: number) => string;
  tooltipFormatter?: TooltipFormatter;
  /// Fade rows flagged `partial`, so the in-progress month reads as "not
  /// finished" rather than "we stopped spending".
  dimPartial?: boolean;
}) {
  const opacity = (row: unknown) =>
    dimPartial && (row as { partial?: boolean } | undefined)?.partial ? PARTIAL_OPACITY : 1;

  return (
    <ResponsiveContainer width="100%" height={280}>
      <ComposedChart data={data} margin={{ top: 4, right: 16, left: 8, bottom: 0 }}>
        <CartesianGrid strokeDasharray="3 3" stroke={GRID} />
        <XAxis
          dataKey="month"
          tick={{ fill: TICK, fontSize: 12 }}
          axisLine={false}
          tickLine={false}
        />
        <YAxis
          allowDecimals={allowDecimals}
          tickFormatter={yTickFormatter}
          tick={{ fill: TICK, fontSize: 11 }}
          axisLine={false}
          tickLine={false}
          width={56}
        />
        <Tooltip
          formatter={tooltipFormatter}
          contentStyle={{
            background: "var(--popover)",
            border: "1px solid var(--border)",
            borderRadius: 8,
          }}
        />
        {stacked && <Legend wrapperStyle={{ fontSize: 12 }} />}
        {type === "bar"
          ? series.map((s, i) => (
              <Bar
                key={s.dataKey}
                dataKey={s.dataKey}
                name={s.name}
                stackId={stacked ? "stack" : undefined}
                fill={s.color}
                radius={i === series.length - 1 ? [4, 4, 0, 0] : undefined}
              >
                {dimPartial &&
                  data.map((row) => (
                    <Cell key={String(row.month)} fill={s.color} fillOpacity={opacity(row)} />
                  ))}
              </Bar>
            ))
          : series.map((s) => (
              <Line
                key={s.dataKey}
                type="monotone"
                dataKey={s.dataKey}
                name={s.name}
                stroke={s.color}
                strokeWidth={2}
                dot={(props: { cx?: number; cy?: number; index?: number; payload?: unknown }) => (
                  <circle
                    key={props.index}
                    cx={props.cx}
                    cy={props.cy}
                    r={3}
                    fill={s.color}
                    fillOpacity={opacity(props.payload)}
                  />
                )}
                activeDot={{ r: 5 }}
              />
            ))}
      </ComposedChart>
    </ResponsiveContainer>
  );
}

const toSeries = (categories: { name: string; color: string }[]): Series[] =>
  categories.map((c) => ({ dataKey: c.name, name: c.name, color: c.color }));

/// The wants budget is 30% of one person's salary, so it has no Joint view.
type Person = "Alex" | "Casey";

export function AnalyticsPage() {
  const { data: session } = useSession();
  // Default the fun-budget owner to whoever's logged in (same pattern as Investments);
  // either person can still switch to view the other's budget.
  const [person, setPerson] = useState<Person>(session?.user.owner === "Casey" ? "Casey" : "Alex");

  const { data, isPending } = useQuery<AnalyticsData>({
    queryKey: ["analytics", person],
    queryFn: () =>
      api.get("/api/dashboard/analytics", { params: { owner: person } }).then((r) => r.data),
  });

  return (
    <div className="max-w-7xl mx-auto space-y-6">
      <div>
        <h1 className="text-3xl font-bold text-foreground">Analytics</h1>
        <p className="text-sm text-muted-foreground uppercase tracking-wide mt-1">Year to date</p>
      </div>

      {/* Top row — budget gauge + monthly fun */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Wants budget gauge */}
        <Card>
          <CardHeader className="pb-2 flex-row items-center justify-between space-y-0">
            <CardTitle className="text-xs uppercase tracking-wide text-muted-foreground">
              {isPending ? "Wants Budget" : `${data!.budget.month} Wants Budget`}
            </CardTitle>
            <Select value={person} onValueChange={(v) => setPerson(v as Person)}>
              <SelectTrigger className="h-7 w-[100px] text-xs" aria-label="Select person">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="Alex">Alex</SelectItem>
                <SelectItem value="Casey">Casey</SelectItem>
              </SelectContent>
            </Select>
          </CardHeader>
          <CardContent>
            {isPending ? <ChartSkeleton /> : <BudgetGauge budget={data!.budget} />}
          </CardContent>
        </Card>

        <ChartCard title="Monthly Wants Spending" data={data} isPending={isPending}>
          {(type, series) => (
            <MonthlyChart
              type={type}
              data={series.monthlyFun}
              series={toSeries(data!.funCategories)}
              stacked
              {...money}
            />
          )}
        </ChartCard>
      </div>

      {/* Transaction volume — full width */}
      <ChartCard
        title="Transactions per Month"
        description="Row count, not spend — a month that dips usually means a statement is missing. The current month is still filling up."
        data={data}
        isPending={isPending}
      >
        {(type, series) => (
          <MonthlyChart
            type={type}
            data={series.monthlyTransactionCount}
            series={[{ dataKey: "count", name: "Transactions", color: "var(--primary)" }]}
            allowDecimals={false}
            tooltipFormatter={(v, _n, item) => [
              `${(v as number).toLocaleString("en-GB")}${item?.payload?.partial ? " so far" : ""}`,
              "Transactions",
            ]}
            dimPartial
          />
        )}
      </ChartCard>

      {/* Vacation — full width */}
      <ChartCard title="Vacation Spending" data={data} isPending={isPending}>
        {(type, series) => (
          <MonthlyChart
            type={type}
            data={series.monthlyVacation}
            series={[{ dataKey: "amount", name: "Vacation", color: data!.vacationColor }]}
            {...money}
          />
        )}
      </ChartCard>

      {/* Row 3 */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <ChartCard title="Food Breakdown" data={data} isPending={isPending}>
          {(type, series) => (
            <MonthlyChart
              type={type}
              data={series.monthlyFood}
              series={toSeries(data!.foodCategories)}
              stacked
              {...money}
            />
          )}
        </ChartCard>

        <ChartCard title="Monthly Golf Spending" data={data} isPending={isPending}>
          {(type, series) => (
            <MonthlyChart
              type={type}
              data={series.monthlyGolf}
              series={[{ dataKey: "amount", name: "Golf", color: "#22c55e" }]}
              {...money}
            />
          )}
        </ChartCard>
      </div>
    </div>
  );
}
