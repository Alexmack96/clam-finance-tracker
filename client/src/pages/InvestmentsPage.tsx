import { useMemo, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  ComposedChart, Area, Line, XAxis, YAxis, CartesianGrid, Tooltip,
  ResponsiveContainer, Legend, PieChart, Pie, Cell,
} from "recharts";
import { AllCommunityModule, ModuleRegistry, themeQuartz } from "ag-grid-community";
import type { ColDef, CellValueChangedEvent, GetRowIdParams } from "ag-grid-community";
import type { CustomCellRendererProps } from "ag-grid-react";
import { AgGridReact } from "ag-grid-react";
import { Card, CardContent, CardHeader, CardTitle } from "../components/ui/card.js";
import { Button } from "../components/ui/button.js";
import { Input } from "../components/ui/input.js";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "../components/ui/dialog.js";
import { Label } from "../components/ui/label.js";
import { TrendingUp, TrendingDown, Minus, Plus, Trash2 } from "lucide-react";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent,
  AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "../components/ui/alert-dialog.js";
import api from "../lib/api.js";

ModuleRegistry.registerModules([AllCommunityModule]);

// ─── Types ────────────────────────────────────────────────────────────────────

type InvCategory = "pension" | "crypto" | "equity" | "cash" | "commodity" | "debt";

interface Snapshot { id: string; date: string; value: number }

interface Account {
  id:        string;
  name:      string;
  category:  InvCategory;
  rate:      number | null;
  sortOrder: number;
  snapshots: Snapshot[];
}

interface InvestmentsData {
  accounts: Account[];
  dates:    string[];   // sorted ISO date strings
  stats: {
    navLatest:    number;
    navPrev:      number;
    dtdPnL:       number;
    mtdPnL:       number;
    ytdPnL:       number;
    itdPnL:       number;
    pension:      number | null;
    pensionPrev:  number | null;
    totalWealth:  number | null;
  };
}

// ─── Constants ────────────────────────────────────────────────────────────────

const CAT_CONFIG: Record<InvCategory | "__total", { label: string; color: string; areaColor: string }> = {
  pension:   { label: "Pension",   color: "#8b5cf6", areaColor: "#8b5cf620" },
  crypto:    { label: "Crypto",    color: "#f97316", areaColor: "#f9731620" },
  equity:    { label: "Equity",    color: "#3b82f6", areaColor: "#3b82f620" },
  cash:      { label: "Cash",      color: "#22c55e", areaColor: "#22c55e20" },
  commodity: { label: "Commodity", color: "#eab308", areaColor: "#eab30820" },
  debt:      { label: "Debt",      color: "#ef4444", areaColor: "#ef444420" },
  __total:   { label: "Total NAV", color: "var(--muted-foreground)", areaColor: "" },
};

const CATEGORY_OPTIONS: InvCategory[] = ["pension", "crypto", "equity", "cash", "commodity", "debt"];

// ─── Helpers ──────────────────────────────────────────────────────────────────

const fmt = (n: number) =>
  new Intl.NumberFormat("en-GB", { style: "currency", currency: "GBP", maximumFractionDigits: 0 }).format(n);

const fmtFull = (n: number) =>
  new Intl.NumberFormat("en-GB", { style: "currency", currency: "GBP", minimumFractionDigits: 0, maximumFractionDigits: 0 }).format(n);

function pct(n: number, base: number) {
  if (base === 0) return "";
  return `${n >= 0 ? "+" : ""}${((n / base) * 100).toFixed(1)}%`;
}

function labelDate(iso: string) {
  const d = new Date(iso);
  return d.toLocaleDateString("en-GB", { month: "short", year: "2-digit" });
}

function fieldKey(iso: string) {
  return `d_${iso.slice(0, 10)}`;
}

/** Sum non-pension accounts at a given date ISO string */
function navAtDate(accounts: Account[], dateIso: string): number {
  const day = dateIso.slice(0, 10);
  return accounts
    .filter((a) => a.category !== "pension")
    .reduce((sum, a) => {
      const snap = a.snapshots.find((s) => s.date.slice(0, 10) === day);
      return sum + (snap?.value ?? 0);
    }, 0);
}

// ─── AG Grid theme ────────────────────────────────────────────────────────────

const gridTheme = themeQuartz.withParams({
  backgroundColor:       "var(--card)",
  foregroundColor:       "var(--foreground)",
  borderColor:           "var(--border)",
  headerBackgroundColor: "var(--card)",
  headerTextColor:       "var(--muted-foreground)",
  subtleTextColor:       "var(--muted-foreground)",
  accentColor:           "var(--primary)",
  rowHoverColor:         "var(--accent)",
  fontFamily:            "inherit",
  fontSize:              12,
  headerHeight:          32,
  rowHeight:             40,
  wrapperBorder:         false,
  wrapperBorderRadius:   0,
  cellHorizontalPadding: 10,
  browserColorScheme:    "inherit",
});

// ─── Cell renderers ───────────────────────────────────────────────────────────

function DateColumnHeader({ displayName, onDelete }: { displayName: string; onDelete: () => void }) {
  return (
    <div className="flex items-center justify-between w-full gap-1 group">
      <span>{displayName}</span>
      <button
        onClick={(e) => { e.stopPropagation(); onDelete(); }}
        className="opacity-0 group-hover:opacity-100 text-muted-foreground/60 hover:text-red-500 transition-all"
        title="Delete snapshot date"
      >
        <Trash2 className="w-3 h-3" />
      </button>
    </div>
  );
}

function CategoryBadge({ value }: { value: InvCategory | "__total" }) {
  const cfg = CAT_CONFIG[value] ?? CAT_CONFIG.__total;
  if (value === "__total") return <span className="text-xs text-muted-foreground font-semibold">—</span>;
  return (
    <span
      className="px-2 py-0.5 rounded-full text-xs font-medium border"
      style={{ color: cfg.color, borderColor: cfg.color }}
    >
      {cfg.label}
    </span>
  );
}

function CategoryRenderer({ value }: CustomCellRendererProps) {
  return <CategoryBadge value={value} />;
}

function ValueRenderer({ value, data }: CustomCellRendererProps) {
  if (value === undefined || value === null || value === "") return <span className="text-muted-foreground/30">—</span>;
  const num = typeof value === "number" ? value : parseFloat(value);
  if (isNaN(num)) return <span className="text-muted-foreground/30">—</span>;
  const isTotal = data?.category === "__total";
  return (
    <span className={`font-${isTotal ? "bold" : "normal"} ${num < 0 ? "text-red-500" : num === 0 ? "text-muted-foreground/40" : ""}`}>
      {fmtFull(num)}
    </span>
  );
}


function PnLCard({ label, value, base }: { label: string; value: number; base: number }) {
  const positive = value > 0;
  const zero = value === 0;
  const Icon = zero ? Minus : positive ? TrendingUp : TrendingDown;
  const color = zero ? "text-muted-foreground" : positive ? "text-green-500" : "text-red-500";
  return (
    <Card>
      <CardHeader className="pb-1">
        <CardTitle className="text-xs uppercase tracking-wide text-muted-foreground">{label}</CardTitle>
      </CardHeader>
      <CardContent>
        <div className="flex items-center gap-2">
          <Icon className={`w-4 h-4 ${color}`} />
          <p className={`text-2xl font-bold ${color}`}>{value >= 0 ? "+" : ""}{fmt(value)}</p>
        </div>
        <p className="text-xs text-muted-foreground mt-1">{pct(value, base)}</p>
      </CardContent>
    </Card>
  );
}

// ─── Custom tooltip ───────────────────────────────────────────────────────────

function ChartTooltip({ active, payload, label }: any) {
  if (!active || !payload?.length) return null;
  return (
    <div className="bg-popover border border-border rounded-md p-3 text-xs space-y-1 shadow-lg">
      <p className="font-semibold text-foreground mb-2">{label}</p>
      {payload.map((entry: any) => (
        <div key={entry.name} className="flex justify-between gap-6">
          <span style={{ color: entry.color }}>{entry.name}</span>
          <span className="font-medium">{fmt(entry.value)}</span>
        </div>
      ))}
    </div>
  );
}

// ─── Page ─────────────────────────────────────────────────────────────────────

export function InvestmentsPage() {
  const queryClient = useQueryClient();
  const [deleteAccountId, setDeleteAccountId] = useState<string | null>(null);
  const [deleteDatePending, setDeleteDatePending] = useState<string | null>(null);
  const [showAddAccount, setShowAddAccount] = useState(false);
  const [showAddDate, setShowAddDate] = useState(false);
  const [localDates, setLocalDates] = useState<string[]>([]);
  const [newDate, setNewDate] = useState(() => {
    // default to end of current month
    const now = new Date();
    const last = new Date(now.getFullYear(), now.getMonth() + 1, 0);
    return last.toISOString().slice(0, 10);
  });
  const [addAccountForm, setAddAccountForm] = useState({
    name: "", category: "cash" as InvCategory, rate: "",
  });

  const { data, isLoading } = useQuery<InvestmentsData>({
    queryKey: ["investments"],
    queryFn: () => api.get("/api/investments").then((r) => r.data),
  });

  const upsertMutation = useMutation({
    mutationFn: (body: { accountId: string; date: string; value: number }) =>
      api.put("/api/investments/snapshots", body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["investments"] }),
  });

  const deleteAccountMutation = useMutation({
    mutationFn: (id: string) => api.delete(`/api/investments/accounts/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["investments"] }),
  });

  const createAccountMutation = useMutation({
    mutationFn: (body: { name: string; category: InvCategory; rate: number | null }) =>
      api.post("/api/investments/accounts", body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["investments"] });
      setShowAddAccount(false);
      setAddAccountForm({ name: "", category: "cash", rate: "" });
    },
  });

  const updateAccountMutation = useMutation({
    mutationFn: ({ id, ...body }: { id: string; name?: string; rate?: number | null }) =>
      api.patch(`/api/investments/accounts/${id}`, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["investments"] }),
  });

  const deleteSnapshotDateMutation = useMutation({
    mutationFn: (day: string) => api.delete(`/api/investments/snapshots/date/${day}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["investments"] }),
  });

  function deleteSnapshotDate(iso: string) {
    setDeleteDatePending(iso.slice(0, 10));
  }

  function confirmDeleteSnapshotDate() {
    if (!deleteDatePending) return;
    setLocalDates((prev) => prev.filter((d) => d !== deleteDatePending));
    deleteSnapshotDateMutation.mutate(deleteDatePending);
    setDeleteDatePending(null);
  }

  // ── Chart data ──────────────────────────────────────────────────────────────

  const chartData = useMemo(() => {
    if (!data) return [];
    return data.dates.map((iso) => {
      const day = iso.slice(0, 10);
      const get = (cat: string) =>
        data.accounts
          .filter((a) => a.category === cat)
          .reduce((s, a) => {
            const snap = a.snapshots.find((sp) => sp.date.slice(0, 10) === day);
            return s + (snap?.value ?? 0);
          }, 0);
      const cash   = get("cash");
      const equity = get("equity");
      const crypto = get("crypto");
      const debt   = Math.abs(get("debt")); // stored negative
      return { label: labelDate(iso), cash, equity, crypto, nav: cash + equity + crypto - debt };
    });
  }, [data]);

  // ── Grid setup ──────────────────────────────────────────────────────────────

  const allDates = useMemo(() => {
    if (!data) return localDates;
    const existing = new Set(data.dates.map((d) => d.slice(0, 10)));
    const extra = localDates.filter((d) => !existing.has(d));
    return [...data.dates, ...extra.map((d) => d + "T00:00:00.000Z")];
  }, [data, localDates]);

  const rowData = useMemo(() => {
    if (!data) return [];
    return data.accounts.map((acc) => {
      const row: Record<string, any> = {
        id: acc.id, name: acc.name, category: acc.category,
        rate: acc.rate, sortOrder: acc.sortOrder,
      };
      for (const snap of acc.snapshots) {
        row[fieldKey(snap.date)] = snap.value;
        row[`${fieldKey(snap.date)}_snapId`] = snap.id;
      }
      return row;
    });
  }, [data]);

  const pinnedBottomRowData = useMemo(() => {
    if (!data) return [];
    const row: Record<string, any> = {
      id: "__total", name: "Total NAV", category: "__total", rate: null,
    };
    for (const iso of allDates) {
      row[fieldKey(iso)] = navAtDate(data.accounts, iso);
    }
    return [row];
  }, [data, allDates]);

  const columnDefs = useMemo((): ColDef[] => {
    const fixed: ColDef[] = [
      {
        field: "name", headerName: "INVESTMENT", pinned: "left", width: 220,
        editable: (p) => p.data?.id !== "__total",
        cellStyle: (p) => p.data?.category === "__total"
          ? { fontWeight: "bold", color: "var(--foreground)" } : null,
      },
      {
        field: "category", headerName: "TYPE", pinned: "left", width: 115,
        cellRenderer: CategoryRenderer,
        sortable: false,
      },
      {
        field: "rate", headerName: "RATE %", pinned: "left", width: 80,
        editable: (p) => p.data?.id !== "__total",
        cellEditor: "agNumberCellEditor",
        valueFormatter: (p) => p.value != null && p.value !== "" ? `${p.value}%` : "—",
      },
      {
        headerName: "", pinned: "left", width: 52, sortable: false, filter: false,
        suppressHeaderMenuButton: true, resizable: false,
        cellRenderer: (p: CustomCellRendererProps) => {
          if (!p.data || p.data.id === "__total") return null;
          return (
            <button
              onClick={() => setDeleteAccountId(p.data.id)}
              className="text-muted-foreground/40 hover:text-red-500 transition-colors p-1"
              title="Delete holding"
            >
              <Trash2 className="w-3.5 h-3.5" />
            </button>
          );
        },
      },
    ];

    const dateCols: ColDef[] = allDates.map((iso) => ({
      field: fieldKey(iso),
      headerName: labelDate(iso),
      headerComponent: DateColumnHeader,
      headerComponentParams: { onDelete: () => deleteSnapshotDate(iso) },
      width: 105,
      editable: (p: any) => p.data?.id !== "__total",
      cellEditor: "agNumberCellEditor",
      cellRenderer: ValueRenderer,
      cellStyle: (p: any) => p.data?.category === "__total"
        ? { fontWeight: "bold", borderTop: "1px solid var(--border)" }
        : null,
      suppressKeyboardEvent: (p: any) => p.event.key === "Escape",
    }));

    return [...fixed, ...dateCols];
  }, [allDates]);

  function onCellValueChanged(e: CellValueChangedEvent) {
    const field = e.colDef.field;
    if (!field || e.data.id === "__total") return;

    if (field === "name") {
      const name = String(e.newValue).trim();
      if (name) updateAccountMutation.mutate({ id: e.data.id, name });
      return;
    }

    if (field === "rate") {
      const raw = String(e.newValue).trim();
      const rate = raw === "" || raw === "—" ? null : parseFloat(raw);
      updateAccountMutation.mutate({ id: e.data.id, rate: isNaN(rate as number) ? null : rate });
      return;
    }

    if (field.startsWith("d_")) {
      const dateStr = field.replace("d_", "");
      const value = parseFloat(String(e.newValue));
      if (isNaN(value)) return;
      upsertMutation.mutate({ accountId: e.data.id, date: dateStr + "T00:00:00.000Z", value });
    }
  }

  function handleAddDate() {
    if (!newDate) return;
    const isoDay = newDate; // YYYY-MM-DD
    // Check if already exists
    const exists = data?.dates.some((d) => d.slice(0, 10) === isoDay) || localDates.includes(isoDay);
    if (!exists) setLocalDates((prev) => [...prev, isoDay].sort());
    setShowAddDate(false);
  }

  function handleAddAccount() {
    if (!addAccountForm.name.trim()) return;
    createAccountMutation.mutate({
      name:     addAccountForm.name.trim(),
      category: addAccountForm.category,
      rate:     addAccountForm.rate ? parseFloat(addAccountForm.rate) : null,
    });
  }

  // ── Derived summary stats ───────────────────────────────────────────────────

  const stats = data?.stats;
  const latestNAV = stats?.navLatest ?? 0;

  // Per-category breakdown for the latest snapshot
  const breakdown = useMemo(() => {
    if (!data) return { cash: 0, equity: 0, crypto: 0, commodity: 0, debt: 0, gross: 0 };
    const lastDate = data.dates.at(-1);
    if (!lastDate) return { cash: 0, equity: 0, crypto: 0, commodity: 0, debt: 0, gross: 0 };
    const day = lastDate.slice(0, 10);
    const totals: Record<string, number> = { cash: 0, equity: 0, crypto: 0, commodity: 0, debt: 0 };
    for (const acc of data.accounts) {
      if (acc.category === "pension") continue;
      const snap = acc.snapshots.find((s) => s.date.slice(0, 10) === day);
      const val = snap?.value ?? 0;
      if (acc.category === "debt") totals.debt += Math.abs(val);
      else if (totals[acc.category] !== undefined) totals[acc.category] += val;
    }
    const gross = totals.cash + totals.equity + totals.crypto + totals.commodity;
    return { ...totals, gross } as { cash: number; equity: number; crypto: number; commodity: number; debt: number; gross: number };
  }, [data]);

  const pieData = useMemo(() => [
    { name: "Cash",        key: "cash",      color: "#22c55e", value: breakdown.cash      },
    { name: "Index Funds", key: "equity",    color: "#3b82f6", value: breakdown.equity    },
    { name: "Crypto",      key: "crypto",    color: "#f97316", value: breakdown.crypto    },
    { name: "Commodities", key: "commodity", color: "#eab308", value: breakdown.commodity },
  ].filter((d) => d.value > 0), [breakdown]);

  if (isLoading) {
    return (
      <div className="max-w-7xl mx-auto space-y-6">
        <div className="h-8 w-48 bg-muted animate-pulse rounded" />
        <div className="grid grid-cols-4 gap-4">
          {Array.from({ length: 8 }).map((_, i) => (
            <div key={i} className="h-24 bg-muted animate-pulse rounded-lg" />
          ))}
        </div>
      </div>
    );
  }

  return (
    <div className="max-w-full px-6 space-y-6">
      {/* ── Header ─────────────────────────────────────────────────────────── */}
      <div className="flex items-end justify-between">
        <div>
          <h1 className="text-3xl font-bold text-foreground">Investments</h1>
          <p className="text-sm text-muted-foreground uppercase tracking-wide mt-1">
            Portfolio snapshot — liquid NAV excludes pension
          </p>
        </div>
        <div className="flex gap-2">
          <Button size="sm" variant="outline" onClick={() => setShowAddDate(true)}>
            <Plus className="w-3.5 h-3.5 mr-1" /> New Snapshot
          </Button>
          <Button size="sm" onClick={() => setShowAddAccount(true)}>
            <Plus className="w-3.5 h-3.5 mr-1" /> Add Holding
          </Button>
        </div>
      </div>

      {/* ── Row 1: Total Wealth + Portfolio Value ───────────────────────────── */}
      <div className="grid grid-cols-2 gap-4">
        <Card className="border-violet-300 dark:border-violet-800">
          <CardHeader className="pb-1">
            <CardTitle className="text-xs uppercase tracking-wide text-muted-foreground">Pension Value</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-5xl font-bold text-violet-500">
              {stats?.pension != null ? fmt(stats.pension) : "—"}
            </p>
            {stats?.pension != null && stats.pensionPrev != null && (
              <p className={`text-sm mt-2 font-medium ${stats.pension - stats.pensionPrev >= 0 ? "text-green-500" : "text-red-500"}`}>
                {stats.pension - stats.pensionPrev >= 0 ? "+" : ""}{fmt(stats.pension - stats.pensionPrev)} vs previous snapshot
              </p>
            )}
          </CardContent>
        </Card>

        <Card className="border-primary/30">
          <CardHeader className="pb-1">
            <CardTitle className="text-xs uppercase tracking-wide text-muted-foreground">Portfolio Value</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-5xl font-bold text-primary">{fmt(latestNAV)}</p>
            {stats && (
              <p className={`text-sm mt-2 font-medium ${stats.dtdPnL >= 0 ? "text-green-500" : "text-red-500"}`}>
                {stats.dtdPnL >= 0 ? "+" : ""}{fmt(stats.dtdPnL)} vs previous snapshot
              </p>
            )}
          </CardContent>
        </Card>
      </div>

      {/* ── Row 2: PnL cards ────────────────────────────────────────────────── */}
      <div className="grid grid-cols-4 gap-4">
        <PnLCard label="MTD P&L"  value={stats?.mtdPnL ?? 0} base={stats ? latestNAV - (stats.mtdPnL) : 1} />
        <PnLCard label="YTD P&L"  value={stats?.ytdPnL ?? 0} base={stats ? latestNAV - (stats.ytdPnL) : 1} />
        <PnLCard label="ITD P&L"  value={stats?.itdPnL ?? 0} base={stats ? latestNAV - (stats.itdPnL) : 1} />
        <PnLCard label="vs Prev Snapshot" value={stats?.dtdPnL ?? 0} base={stats?.navPrev ?? 1} />
      </div>

      {/* ── Row 3: Trends + Allocation ────────────────────────────────────── */}
      <div className="grid grid-cols-3 gap-4">
        {/* Area chart: 2 cols */}
        <Card className="col-span-2">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs uppercase tracking-wide text-muted-foreground">Portfolio Composition Over Time</CardTitle>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={280}>
              <ComposedChart data={chartData} margin={{ top: 10, right: 20, left: 10, bottom: 0 }}>
                <defs>
                  <linearGradient id="cashGrad" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%"  stopColor="#22c55e" stopOpacity={0.45} />
                    <stop offset="95%" stopColor="#22c55e" stopOpacity={0.05} />
                  </linearGradient>
                  <linearGradient id="equityGrad" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%"  stopColor="#3b82f6" stopOpacity={0.45} />
                    <stop offset="95%" stopColor="#3b82f6" stopOpacity={0.05} />
                  </linearGradient>
                  <linearGradient id="cryptoGrad" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%"  stopColor="#f97316" stopOpacity={0.45} />
                    <stop offset="95%" stopColor="#f97316" stopOpacity={0.05} />
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" stroke="rgba(128,128,128,0.12)" vertical={false} />
                <XAxis dataKey="label" tick={{ fontSize: 10, fill: "#888" }} interval={1} />
                <YAxis tickFormatter={(v) => `£${(v / 1000).toFixed(0)}k`} tick={{ fontSize: 10, fill: "#888" }} domain={[0, "auto"]} />
                <Tooltip content={<ChartTooltip />} />
                <Legend wrapperStyle={{ fontSize: 11 }} />
                <Area stackId="a" type="monotone" dataKey="cash"   name="Cash"        stroke="#22c55e" fill="url(#cashGrad)"   strokeWidth={1.5} />
                <Area stackId="a" type="monotone" dataKey="equity" name="Index Funds" stroke="#3b82f6" fill="url(#equityGrad)" strokeWidth={1.5} />
                <Area stackId="a" type="monotone" dataKey="crypto" name="Crypto"      stroke="#f97316" fill="url(#cryptoGrad)" strokeWidth={1.5} />
                <Line type="monotone" dataKey="nav" name="NAV (net)" stroke="oklch(0.527 0.154 150.069)" strokeWidth={2.5} dot={false} activeDot={{ r: 4 }} />
              </ComposedChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>

        {/* Donut: current allocation */}
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-xs uppercase tracking-wide text-muted-foreground">
              Current Allocation
              <span className="ml-2 text-muted-foreground/50 normal-case font-normal">
                {data?.dates.at(-1) ? labelDate(data.dates.at(-1)!) : ""}
              </span>
            </CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col items-center gap-4">
            <div className="relative">
              <PieChart width={180} height={180}>
                <Pie
                  data={pieData}
                  cx={90} cy={90}
                  innerRadius={54} outerRadius={80}
                  dataKey="value"
                  strokeWidth={0}
                >
                  {pieData.map((entry) => (
                    <Cell key={entry.key} fill={entry.color} />
                  ))}
                </Pie>
                <Tooltip formatter={(v: number) => fmt(v)} />
              </PieChart>
              {/* Centre label */}
              <div className="absolute inset-0 flex flex-col items-center justify-center pointer-events-none">
                <span className="text-xs text-muted-foreground">NAV</span>
                <span className="text-sm font-bold text-foreground">{fmt(latestNAV)}</span>
              </div>
            </div>
            <div className="w-full space-y-2">
              {pieData.map((d) => (
                <div key={d.key} className="grid grid-cols-[1fr_auto_3rem] items-center gap-1 text-xs">
                  <span className="flex items-center gap-1.5 min-w-0">
                    <span className="w-2 h-2 rounded-full inline-block shrink-0" style={{ background: d.color }} />
                    {d.name}
                  </span>
                  <span className="font-medium text-right">{fmt(d.value)}</span>
                  <span className="text-muted-foreground text-right">
                    {breakdown.gross > 0 ? `${((d.value / breakdown.gross) * 100).toFixed(1)}%` : "—"}
                  </span>
                </div>
              ))}
              {breakdown.debt > 0 && (
                <div className="grid grid-cols-[1fr_auto_3rem] items-center gap-1 text-xs border-t border-border pt-2 mt-1">
                  <span className="flex items-center gap-1.5 min-w-0">
                    <span className="w-2 h-2 rounded-full inline-block shrink-0 bg-red-500" />
                    Debt
                  </span>
                  <span className="font-medium text-right text-red-500">-{fmt(breakdown.debt)}</span>
                  <span className="text-muted-foreground text-right" />
                </div>
              )}
            </div>
          </CardContent>
        </Card>
      </div>

      {/* ── Grid ─────────────────────────────────────────────────────────────── */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-xs uppercase tracking-wide text-muted-foreground">
            Holdings — click any value cell to edit
          </CardTitle>
        </CardHeader>
        <CardContent className="p-0 overflow-hidden">
          <AgGridReact
            theme={gridTheme}
            rowData={rowData}
            columnDefs={columnDefs}
            pinnedBottomRowData={pinnedBottomRowData}
            domLayout="autoHeight"
            defaultColDef={{ sortable: true, resizable: true, suppressMovable: true }}
            suppressCellFocus={false}
            getRowId={(p: GetRowIdParams) => p.data.id}
            onCellValueChanged={onCellValueChanged}
            singleClickEdit
            stopEditingWhenCellsLoseFocus
          />
        </CardContent>
      </Card>

      {/* ── Add Snapshot Date dialog ─────────────────────────────────────────── */}
      <Dialog open={showAddDate} onOpenChange={setShowAddDate}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Add Snapshot Date</DialogTitle>
          </DialogHeader>
          <div className="space-y-3 py-2">
            <p className="text-sm text-muted-foreground">
              Add a new date column to the grid. Enter values directly in the table after closing.
            </p>
            <div className="space-y-1">
              <Label>Snapshot date (use month-end)</Label>
              <Input type="date" value={newDate} onChange={(e) => setNewDate(e.target.value)} />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowAddDate(false)}>Cancel</Button>
            <Button onClick={handleAddDate}>Add Column</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* ── Add Account dialog ───────────────────────────────────────────────── */}
      <Dialog open={showAddAccount} onOpenChange={setShowAddAccount}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Add Holding</DialogTitle>
          </DialogHeader>
          <div className="space-y-3 py-2">
            <div className="space-y-1">
              <Label>Name</Label>
              <Input
                placeholder="e.g. Equity — iShares FTSE 100"
                value={addAccountForm.name}
                onChange={(e) => setAddAccountForm((f) => ({ ...f, name: e.target.value }))}
              />
            </div>
            <div className="space-y-1">
              <Label>Category</Label>
              <select
                value={addAccountForm.category}
                onChange={(e) => setAddAccountForm((f) => ({ ...f, category: e.target.value as InvCategory }))}
                className="w-full border border-input bg-background text-foreground rounded-md px-3 py-2 text-sm"
              >
                {CATEGORY_OPTIONS.map((c) => (
                  <option key={c} value={c}>{CAT_CONFIG[c].label}</option>
                ))}
              </select>
            </div>
            <div className="space-y-1">
              <Label>Interest / target rate % (optional)</Label>
              <Input
                type="number"
                placeholder="e.g. 5.1"
                value={addAccountForm.rate}
                onChange={(e) => setAddAccountForm((f) => ({ ...f, rate: e.target.value }))}
              />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowAddAccount(false)}>Cancel</Button>
            <Button
              onClick={handleAddAccount}
              disabled={!addAccountForm.name.trim() || createAccountMutation.isPending}
            >
              Add
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* ── Delete snapshot date confirmation ───────────────────────────────── */}
      <AlertDialog open={!!deleteDatePending} onOpenChange={(open) => !open && setDeleteDatePending(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete snapshot date?</AlertDialogTitle>
            <AlertDialogDescription>
              This will permanently delete all values for <strong>{deleteDatePending}</strong> across every account. Cannot be undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction className="bg-red-500 hover:bg-red-600 text-white" onClick={confirmDeleteSnapshotDate}>
              Delete
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/* ── Delete account confirmation ──────────────────────────────────────── */}
      <AlertDialog open={!!deleteAccountId} onOpenChange={(open) => !open && setDeleteAccountId(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete holding?</AlertDialogTitle>
            <AlertDialogDescription>
              This will permanently delete this account and all its historical snapshots. Cannot be undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              className="bg-red-500 hover:bg-red-600 text-white"
              onClick={() => {
                if (deleteAccountId) deleteAccountMutation.mutate(deleteAccountId);
                setDeleteAccountId(null);
              }}
            >
              Delete
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
