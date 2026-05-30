import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { AllCommunityModule, ModuleRegistry, themeQuartz } from "ag-grid-community";
import type { ColDef, GridApi } from "ag-grid-community";
import { AgGridReact, useGridFilter } from "ag-grid-react";
import type { CustomCellRendererProps, CustomFilterProps } from "ag-grid-react";
import { Button } from "../components/ui/button.js";
import { Input } from "../components/ui/input.js";
import { Card, CardContent, CardHeader, CardTitle } from "../components/ui/card.js";
import api from "../lib/api.js";

ModuleRegistry.registerModules([AllCommunityModule]);

// ─── Theme ────────────────────────────────────────────────────────────────────

const gridTheme = themeQuartz.withParams({
  backgroundColor:      "var(--card)",
  foregroundColor:      "var(--foreground)",
  borderColor:          "var(--border)",
  headerBackgroundColor:"var(--card)",
  headerTextColor:      "var(--muted-foreground)",
  subtleTextColor:      "var(--muted-foreground)",
  accentColor:          "var(--primary)",
  menuBackgroundColor:  "var(--popover)",
  menuTextColor:        "var(--popover-foreground)",
  rowHoverColor:        "transparent",
  fontFamily:           "inherit",
  fontSize:             13,
  headerHeight:         36,
  rowHeight:            48,
  wrapperBorder:        false,
  wrapperBorderRadius:  0,
  cellHorizontalPadding:8,
  browserColorScheme:   "inherit",
});

// ─── Types ────────────────────────────────────────────────────────────────────

type Owner = "Alex" | "Casey" | "Joint";

interface Category { id: string; name: string; color: string; }

type BankSource = "Monzo" | "Amex" | "Barclays" | "Santander" | "HSBC" | "SoFi" | "Chase" | "Manual";

interface Transaction {
  id: string;
  description: string;
  note: string | null;
  amount: string;
  type: "Income" | "Expense";
  date: string;
  category: Category;
  categoryId: string;
  owner: Owner;
  externalId: string | null;
  reviewed: boolean;
}

interface Summary {
  caseyIn: number;
  jointExpenses: number;
  settlement: number;
  spendingByCategory: { name: string; color: string; value: number }[];
}

interface GridCtx {
  update: (id: string, data: { note?: string | null; categoryId?: string; owner?: Owner; reviewed?: boolean }) => void;
  categories: Category[];
  registerNoteEdit: (id: string, fn: () => void) => void;
  unregisterNoteEdit: (id: string) => void;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function bankSource(externalId: string | null): BankSource {
  if (!externalId) return "Manual";
  if (externalId.startsWith("monzo:"))     return "Monzo";
  if (externalId.startsWith("amex:"))      return "Amex";
  if (externalId.startsWith("barclays:"))  return "Barclays";
  if (externalId.startsWith("santander:")) return "Santander";
  if (externalId.startsWith("hsbc:"))      return "HSBC";
  if (externalId.startsWith("sofi:"))      return "SoFi";
  if (externalId.startsWith("chase:"))     return "Chase";
  return "Manual";
}

const SOURCE_STYLES: Record<BankSource, string> = {
  Monzo:     "text-orange-500 border-orange-500",
  Amex:      "text-blue-500 border-blue-500",
  Barclays:  "text-sky-500 border-sky-500",
  Santander: "text-red-500 border-red-500",
  HSBC:      "text-purple-500 border-purple-500",
  SoFi:      "text-green-500 border-green-500",
  Chase:     "text-blue-700 border-blue-700",
  Manual:    "text-muted-foreground border-muted-foreground/40",
};

const OWNER_STYLES: Record<Owner, string> = {
  Alex:  "text-blue-500 border-blue-500",
  Casey: "text-pink-500 border-pink-500",
  Joint: "text-muted-foreground border-muted-foreground/40",
};

const fmt = (n: number) =>
  new Intl.NumberFormat("en-GB", { style: "currency", currency: "GBP" }).format(n);

// ─── Inline cell components ───────────────────────────────────────────────────

function NoteCell({ tx, onSave, registerNoteEdit, unregisterNoteEdit }: {
  tx: Transaction;
  onSave: (note: string | null) => void;
  registerNoteEdit: (id: string, fn: () => void) => void;
  unregisterNoteEdit: (id: string) => void;
}) {
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState(tx.note ?? "");
  const inputRef = useRef<HTMLInputElement>(null);

  function commit() {
    setEditing(false);
    const trimmed = value.trim();
    const next = trimmed === "" ? null : trimmed;
    if (next !== tx.note) onSave(next);
  }

  const start = useCallback(() => {
    setValue(tx.note ?? "");
    setEditing(true);
    setTimeout(() => inputRef.current?.focus(), 0);
  }, [tx.note]);

  useEffect(() => {
    registerNoteEdit(tx.id, start);
    return () => unregisterNoteEdit(tx.id);
  }, [tx.id, start, registerNoteEdit, unregisterNoteEdit]);

  if (editing) {
    return (
      <input
        ref={inputRef}
        value={value}
        onChange={(e) => setValue(e.target.value)}
        onBlur={commit}
        onKeyDown={(e) => {
          if (e.key === "Enter")  commit();
          if (e.key === "Escape") { setValue(tx.note ?? ""); setEditing(false); }
          if (["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End"].includes(e.key)) e.stopPropagation();
        }}
        className="w-full bg-transparent border-b border-primary outline-none text-sm py-0.5"
      />
    );
  }

  return (
    <span onDoubleClick={start} className="cursor-text select-text text-sm group" title="Double-click to edit note">
      {tx.note
        ? <span>{tx.note}</span>
        : <span className="text-muted-foreground/40 group-hover:text-muted-foreground italic">Add note…</span>
      }
    </span>
  );
}

function CategoryCell({ tx, categories, onSave }: { tx: Transaction; categories: Category[]; onSave: (id: string) => void }) {
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLSpanElement>(null);
  const [pos, setPos] = useState({ top: 0, left: 0 });

  useEffect(() => {
    if (!open) return;
    function handle() { setOpen(false); }
    document.addEventListener("mousedown", handle);
    return () => document.removeEventListener("mousedown", handle);
  }, [open]);

  const DROPDOWN_HEIGHT = categories.length * 32 + 8;

  function handleClick() {
    if (triggerRef.current) {
      const rect = triggerRef.current.getBoundingClientRect();
      const spaceBelow = window.innerHeight - rect.bottom;
      const top = spaceBelow >= DROPDOWN_HEIGHT + 4
        ? rect.bottom + 4
        : rect.top - DROPDOWN_HEIGHT - 4;
      setPos({ top, left: rect.left });
    }
    setOpen((o) => !o);
  }

  return (
    <>
      <span
        ref={triggerRef}
        onClick={handleClick}
        className="cursor-pointer px-2 py-0.5 rounded-full text-xs font-medium border hover:opacity-80"
        style={{ color: tx.category.color, borderColor: tx.category.color }}
      >
        {tx.category.name}
      </span>
      {open && createPortal(
        <div
          style={{ position: "fixed", top: pos.top, left: pos.left, zIndex: 99999 }}
          className="bg-popover border border-border rounded-md shadow-lg py-1 min-w-[150px]"
          onMouseDown={(e) => e.stopPropagation()}
        >
          {categories.map((c) => (
            <div key={c.id} onClick={() => { onSave(c.id); setOpen(false); }} className="px-2 py-1 cursor-pointer hover:bg-accent">
              <span className="px-2 py-0.5 rounded-full text-xs font-medium border" style={{ color: c.color, borderColor: c.color }}>
                {c.name}
              </span>
            </div>
          ))}
        </div>,
        document.body
      )}
    </>
  );
}

function OwnerCell({ tx, onSave }: { tx: Transaction; onSave: (owner: Owner) => void }) {
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLSpanElement>(null);
  const [pos, setPos] = useState({ top: 0, left: 0 });

  useEffect(() => {
    if (!open) return;
    function handle() { setOpen(false); }
    document.addEventListener("mousedown", handle);
    return () => document.removeEventListener("mousedown", handle);
  }, [open]);

  const OWNER_DROPDOWN_HEIGHT = 3 * 32 + 8;

  function handleClick() {
    if (triggerRef.current) {
      const rect = triggerRef.current.getBoundingClientRect();
      const spaceBelow = window.innerHeight - rect.bottom;
      const top = spaceBelow >= OWNER_DROPDOWN_HEIGHT + 4
        ? rect.bottom + 4
        : rect.top - OWNER_DROPDOWN_HEIGHT - 4;
      setPos({ top, left: rect.left });
    }
    setOpen((o) => !o);
  }

  return (
    <>
      <span
        ref={triggerRef}
        onClick={handleClick}
        className={`cursor-pointer px-2 py-0.5 rounded-full text-xs font-medium border hover:opacity-80 ${OWNER_STYLES[tx.owner]}`}
      >
        {tx.owner}
      </span>
      {open && createPortal(
        <div
          style={{ position: "fixed", top: pos.top, left: pos.left, zIndex: 99999 }}
          className="bg-popover border border-border rounded-md shadow-lg py-1 min-w-[100px]"
          onMouseDown={(e) => e.stopPropagation()}
        >
          {(["Alex", "Casey", "Joint"] as Owner[]).map((o) => (
            <div key={o} onClick={() => { onSave(o); setOpen(false); }} className="px-2 py-1 cursor-pointer hover:bg-accent">
              <span className={`px-2 py-0.5 rounded-full text-xs font-medium border ${OWNER_STYLES[o]}`}>{o}</span>
            </div>
          ))}
        </div>,
        document.body
      )}
    </>
  );
}

// ─── Custom filters ───────────────────────────────────────────────────────────

interface FilterModel { value: string }

interface CategoryFilterExtraProps { categories: Category[] }

function CategoryFilter({
  model,
  onModelChange,
  categories,
}: CustomFilterProps<Transaction, any, FilterModel> & CategoryFilterExtraProps) {
  useGridFilter({
    doesFilterPass: (params) => {
      if (!model) return true;
      return (params.data as Transaction)?.category?.name === model.value;
    },
  });

  const selected = model?.value ?? null;

  return (
    <div className="p-2 min-w-[160px] max-h-[260px] overflow-y-auto">
      <div
        onClick={() => onModelChange(null)}
        className={`px-2 py-1 cursor-pointer rounded text-xs text-muted-foreground mb-1 hover:bg-accent ${!selected ? "bg-accent" : ""}`}
      >
        All
      </div>
      {categories.map((c) => (
        <div
          key={c.id}
          onClick={() => onModelChange({ value: c.name })}
          className={`px-2 py-1 cursor-pointer rounded hover:bg-accent ${selected === c.name ? "bg-accent" : ""}`}
        >
          <span
            className="px-2 py-0.5 rounded-full text-xs font-medium border"
            style={{ color: c.color, borderColor: c.color }}
          >
            {c.name}
          </span>
        </div>
      ))}
    </div>
  );
}

const OWNERS: Owner[] = ["Alex", "Casey", "Joint"];

function OwnerFilter({ model, onModelChange }: CustomFilterProps<Transaction, any, FilterModel>) {
  useGridFilter({
    doesFilterPass: (params) => {
      if (!model) return true;
      return (params.data as Transaction)?.owner === model.value;
    },
  });

  const selected = model?.value ?? null;

  return (
    <div className="p-2 min-w-[130px]">
      <div
        onClick={() => onModelChange(null)}
        className={`px-2 py-1 cursor-pointer rounded text-xs text-muted-foreground mb-1 hover:bg-accent ${!selected ? "bg-accent" : ""}`}
      >
        All
      </div>
      {OWNERS.map((o) => (
        <div
          key={o}
          onClick={() => onModelChange({ value: o })}
          className={`px-2 py-1 cursor-pointer rounded hover:bg-accent ${selected === o ? "bg-accent" : ""}`}
        >
          <span className={`px-2 py-0.5 rounded-full text-xs font-medium border ${OWNER_STYLES[o]}`}>
            {o}
          </span>
        </div>
      ))}
    </div>
  );
}

const SOURCES: BankSource[] = ["Monzo", "Amex", "Barclays", "Santander", "HSBC", "SoFi", "Chase", "Manual"];

function SourceFilter({ model, onModelChange }: CustomFilterProps<Transaction, any, FilterModel>) {
  useGridFilter({
    doesFilterPass: (params) => {
      if (!model) return true;
      return bankSource((params.data as Transaction)?.externalId) === model.value;
    },
  });

  const selected = model?.value ?? null;

  return (
    <div className="p-2 min-w-[140px]">
      <div
        onClick={() => onModelChange(null)}
        className={`px-2 py-1 cursor-pointer rounded text-xs text-muted-foreground mb-1 hover:bg-accent ${!selected ? "bg-accent" : ""}`}
      >
        All
      </div>
      {SOURCES.map((s) => (
        <div
          key={s}
          onClick={() => onModelChange({ value: s })}
          className={`px-2 py-1 cursor-pointer rounded hover:bg-accent ${selected === s ? "bg-accent" : ""}`}
        >
          <span className={`px-2 py-0.5 rounded-full text-xs font-medium border ${SOURCE_STYLES[s]}`}>
            {s}
          </span>
        </div>
      ))}
    </div>
  );
}

interface ReviewedFilterModel { value: "reviewed" | "unreviewed" }

function ReviewedFilter({ model, onModelChange }: CustomFilterProps<Transaction, any, ReviewedFilterModel>) {
  useGridFilter({
    doesFilterPass: (params) => {
      if (!model) return true;
      const reviewed = (params.data as Transaction)?.reviewed ?? false;
      return model.value === "reviewed" ? reviewed : !reviewed;
    },
  });

  const selected = model?.value ?? null;
  const options: { value: "reviewed" | "unreviewed"; label: string; className: string }[] = [
    { value: "unreviewed", label: "Unreviewed", className: "text-amber-600 border-amber-600" },
    { value: "reviewed",   label: "Reviewed",   className: "text-green-600 border-green-600" },
  ];

  return (
    <div className="p-2 min-w-[140px]">
      <div
        onClick={() => onModelChange(null)}
        className={`px-2 py-1 cursor-pointer rounded text-xs text-muted-foreground mb-1 hover:bg-accent ${!selected ? "bg-accent" : ""}`}
      >
        All
      </div>
      {options.map((o) => (
        <div
          key={o.value}
          onClick={() => onModelChange({ value: o.value })}
          className={`px-2 py-1 cursor-pointer rounded hover:bg-accent ${selected === o.value ? "bg-accent" : ""}`}
        >
          <span className={`px-2 py-0.5 rounded-full text-xs font-medium border ${o.className}`}>
            {o.label}
          </span>
        </div>
      ))}
    </div>
  );
}

// ─── Cell renderers (outside component to avoid recreation on render) ─────────

function NoteRenderer({ data, context }: CustomCellRendererProps<Transaction, string, GridCtx>) {
  if (!data) return null;
  return (
    <NoteCell
      tx={data}
      onSave={(note) => context.update(data.id, { note })}
      registerNoteEdit={context.registerNoteEdit}
      unregisterNoteEdit={context.unregisterNoteEdit}
    />
  );
}

function CategoryRenderer({ data, context }: CustomCellRendererProps<Transaction, string, GridCtx>) {
  if (!data) return null;
  return <CategoryCell tx={data} categories={context.categories} onSave={(categoryId) => context.update(data.id, { categoryId })} />;
}

function OwnerRenderer({ data, context }: CustomCellRendererProps<Transaction, string, GridCtx>) {
  if (!data) return null;
  return <OwnerCell tx={data} onSave={(owner) => context.update(data.id, { owner })} />;
}

function SourceRenderer({ data }: CustomCellRendererProps<Transaction>) {
  if (!data) return null;
  const source = bankSource(data.externalId);
  return (
    <span className={`px-2 py-0.5 rounded-full text-xs font-medium border ${SOURCE_STYLES[source]}`}>
      {source}
    </span>
  );
}

function AmountRenderer({ data }: CustomCellRendererProps<Transaction>) {
  if (!data) return null;
  return (
    <span className={`font-numeric font-semibold ${data.type === "Income" ? "text-[var(--signal)]" : "text-destructive"}`}>
      {data.type === "Income" ? "+" : "−"}{fmt(Math.abs(parseFloat(data.amount)))}
    </span>
  );
}

function ReviewedRenderer({ data, context }: CustomCellRendererProps<Transaction, boolean, GridCtx>) {
  if (!data) return null;
  return (
    <button
      onClick={() => context.update(data.id, { reviewed: !data.reviewed })}
      title={data.reviewed ? "Mark as unreviewed" : "Mark as reviewed"}
      className={`w-5 h-5 rounded-full border-2 flex items-center justify-center transition-colors ${
        data.reviewed
          ? "bg-green-500 border-green-500 text-white"
          : "border-muted-foreground/40 hover:border-green-400"
      }`}
    >
      {data.reviewed && <span className="text-[10px] leading-none">✓</span>}
    </button>
  );
}


// ─── Mobile card components ───────────────────────────────────────────────────

function MobileNoteCell({ tx, onSave }: { tx: Transaction; onSave: (note: string | null) => void }) {
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState(tx.note ?? "");
  const inputRef = useRef<HTMLInputElement>(null);

  function commit() {
    setEditing(false);
    const trimmed = value.trim();
    const next = trimmed === "" ? null : trimmed;
    if (next !== tx.note) onSave(next);
  }

  function start() {
    setValue(tx.note ?? "");
    setEditing(true);
    setTimeout(() => inputRef.current?.focus(), 0);
  }

  if (editing) {
    return (
      <input
        ref={inputRef}
        value={value}
        onChange={(e) => setValue(e.target.value)}
        onBlur={commit}
        onKeyDown={(e) => {
          if (e.key === "Enter") commit();
          if (e.key === "Escape") { setValue(tx.note ?? ""); setEditing(false); }
        }}
        className="w-full bg-transparent border-b border-primary outline-none text-sm py-0.5"
      />
    );
  }

  return (
    <button onClick={start} className="text-left w-full">
      {tx.note
        ? <span className="text-sm text-muted-foreground">{tx.note}</span>
        : <span className="text-xs text-muted-foreground/40 italic">Add note…</span>
      }
    </button>
  );
}

type UpdatePatch = { note?: string | null; categoryId?: string; owner?: Owner; reviewed?: boolean };

function MobileTransactionCard({ tx, categories, onUpdate }: {
  tx: Transaction;
  categories: Category[];
  onUpdate: (id: string, patch: UpdatePatch) => void;
}) {
  const source = bankSource(tx.externalId);
  return (
    <div className="py-3 space-y-2">
      <div className="flex items-start justify-between gap-3">
        <div className="flex-1 min-w-0">
          <p className="text-sm font-medium leading-snug">{tx.description}</p>
          <p className="text-xs text-muted-foreground font-numeric mt-0.5">
            {new Date(tx.date).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" })}
          </p>
        </div>
        <div className="flex items-center gap-2.5 shrink-0">
          <span className={`font-numeric font-semibold text-sm ${tx.type === "Income" ? "text-[var(--signal)]" : "text-destructive"}`}>
            {tx.type === "Income" ? "+" : "−"}{fmt(Math.abs(parseFloat(tx.amount)))}
          </span>
          <button
            onClick={() => onUpdate(tx.id, { reviewed: !tx.reviewed })}
            className={`w-5 h-5 rounded-full border-2 flex items-center justify-center transition-colors shrink-0 ${
              tx.reviewed ? "bg-green-500 border-green-500 text-white" : "border-muted-foreground/40"
            }`}
          >
            {tx.reviewed && <span className="text-[10px] leading-none">✓</span>}
          </button>
        </div>
      </div>
      <div className="flex items-center gap-1.5 flex-wrap">
        <CategoryCell tx={tx} categories={categories} onSave={(categoryId) => onUpdate(tx.id, { categoryId })} />
        <OwnerCell tx={tx} onSave={(owner) => onUpdate(tx.id, { owner })} />
        <span className={`px-2 py-0.5 rounded-full text-xs font-medium border ${SOURCE_STYLES[source]}`}>
          {source}
        </span>
      </div>
      <MobileNoteCell tx={tx} onSave={(note) => onUpdate(tx.id, { note })} />
    </div>
  );
}

// ─── Page ─────────────────────────────────────────────────────────────────────

export function DashboardPage() {
  const queryClient = useQueryClient();
  const gridApiRef = useRef<GridApi<Transaction>>();
  const noteEditTriggers = useRef<Map<string, () => void>>(new Map());
  const registerNoteEdit = useCallback((id: string, fn: () => void) => { noteEditTriggers.current.set(id, fn); }, []);
  const unregisterNoteEdit = useCallback((id: string) => { noteEditTriggers.current.delete(id); }, []);
  const [hasFilters, setHasFilters] = useState(false);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [filteredCount, setFilteredCount] = useState<number | null>(null);
  const [filteredSum, setFilteredSum] = useState<number | null>(null);
  const [form, setForm] = useState({
    description: "",
    amount: "",
    type: "Expense" as "Income" | "Expense",
    categoryId: "",
  });

  const { data: summary } = useQuery<Summary>({
    queryKey: ["summary"],
    queryFn: () => api.get("/api/dashboard/summary").then((r) => r.data),
  });

  const { data: categories = [] } = useQuery<Category[]>({
    queryKey: ["categories"],
    queryFn: () => api.get("/api/categories").then((r) => r.data),
  });

  const { data: transactions = [] } = useQuery<Transaction[]>({
    queryKey: ["transactions"],
    queryFn: () => api.get("/api/transactions").then((r) => r.data),
  });

  useEffect(() => {
    if (categories.length > 0 && !form.categoryId) {
      setForm((f) => ({ ...f, categoryId: categories[0].id }));
    }
  }, [categories, form.categoryId]);

  const addMutation = useMutation({
    mutationFn: (data: { description: string; amount: number; type: string; categoryId: string }) =>
      api.post("/api/transactions", data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["transactions"] });
      queryClient.invalidateQueries({ queryKey: ["summary"] });
      setForm((f) => ({ ...f, description: "", amount: "" }));
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, ...data }: { id: string; note?: string | null; categoryId?: string; owner?: Owner; reviewed?: boolean }) =>
      api.patch<Transaction>(`/api/transactions/${id}`, data).then((r) => r.data),
    onSuccess: (updated, variables) => {
      // Sync query cache with server truth
      queryClient.setQueryData<Transaction[]>(["transactions"], (prev) =>
        prev ? prev.map((t) => (t.id === updated.id ? updated : t)) : prev,
      );
      // Re-apply server truth to grid (handles any server-side normalization)
      gridApiRef.current?.applyTransaction({ update: [updated] });
      if (variables.categoryId !== undefined || variables.owner !== undefined) {
        queryClient.invalidateQueries({ queryKey: ["summary"] });
      }
    },
    onError: (_, variables) => {
      // Roll back grid to cached truth
      const cached = queryClient.getQueryData<Transaction[]>(["transactions"]);
      const original = cached?.find((t) => t.id === variables.id);
      if (original) gridApiRef.current?.applyTransaction({ update: [original] });
    },
  });

  const bulkReviewMutation = useMutation({
    mutationFn: ({ ids, reviewed }: { ids: string[]; reviewed: boolean }) =>
      Promise.all(ids.map((id) => api.patch(`/api/transactions/${id}`, { reviewed }))),
    onSuccess: (_, { ids, reviewed }) => {
      const idSet = new Set(ids);
      queryClient.setQueryData<Transaction[]>(["transactions"], (prev) =>
        prev ? prev.map((t) => (idSet.has(t.id) ? { ...t, reviewed } : t)) : prev,
      );
      gridApiRef.current?.deselectAll();
    },
  });

  function getFilteredIds(): string[] {
    const ids: string[] = [];
    gridApiRef.current?.forEachNodeAfterFilter((node) => { if (node.data) ids.push(node.data.id); });
    return ids;
  }

  function handleBulkReview(reviewed: boolean) {
    const ids = selectedIds.length > 0 ? selectedIds : getFilteredIds();
    if (ids.length === 0) return;
    // Immediately update the grid — don't wait for server
    const idSet = new Set(ids);
    const updates: Transaction[] = [];
    gridApiRef.current?.forEachNode((node) => {
      if (node.data && idSet.has(node.data.id)) updates.push({ ...node.data, reviewed });
    });
    if (updates.length > 0) gridApiRef.current?.applyTransaction({ update: updates });
    bulkReviewMutation.mutate({ ids, reviewed });
  }

  function handleAdd() {
    if (!form.description || !form.amount || !form.categoryId) return;
    addMutation.mutate({
      description: form.description,
      amount: parseFloat(form.amount),
      type: form.type,
      categoryId: form.categoryId,
    });
  }

  const sortedTransactions = useMemo(
    () => [...transactions].sort((a, b) => b.date.localeCompare(a.date)),
    [transactions],
  );

  // ─── Mobile filters ────────────────────────────────────────────────────────
  const [mobileSearch, setMobileSearch] = useState("");
  const [mobileOwner, setMobileOwner] = useState<"All" | Owner>("All");
  const [mobileStatus, setMobileStatus] = useState<"All" | "Unreviewed" | "Reviewed">("All");
  const [mobileCategoryId, setMobileCategoryId] = useState<string>("All");
  const [mobileSource, setMobileSource] = useState<"All" | BankSource>("All");
  const [mobileFiltersOpen, setMobileFiltersOpen] = useState(false);

  const mobileTransactions = useMemo(() => {
    const q = mobileSearch.trim().toLowerCase();
    return sortedTransactions.filter((tx) => {
      if (mobileOwner !== "All" && tx.owner !== mobileOwner) return false;
      if (mobileStatus === "Reviewed" && !tx.reviewed) return false;
      if (mobileStatus === "Unreviewed" && tx.reviewed) return false;
      if (mobileCategoryId !== "All" && tx.categoryId !== mobileCategoryId) return false;
      if (mobileSource !== "All" && bankSource(tx.externalId) !== mobileSource) return false;
      if (q && !tx.description.toLowerCase().includes(q) && !(tx.note ?? "").toLowerCase().includes(q)) return false;
      return true;
    });
  }, [sortedTransactions, mobileSearch, mobileOwner, mobileStatus, mobileCategoryId, mobileSource]);

  const mobileFiltersActive =
    mobileSearch.trim() !== "" ||
    mobileOwner !== "All" ||
    mobileStatus !== "All" ||
    mobileCategoryId !== "All" ||
    mobileSource !== "All";

  function resetMobileFilters() {
    setMobileSearch("");
    setMobileOwner("All");
    setMobileStatus("All");
    setMobileCategoryId("All");
    setMobileSource("All");
  }

  function handleMobileUpdate(id: string, patch: UpdatePatch) {
    // Optimistic update in the query cache so cards re-render immediately
    queryClient.setQueryData<Transaction[]>(["transactions"], (prev) =>
      prev ? prev.map((t) => {
        if (t.id !== id) return t;
        const updated = { ...t, ...patch };
        if (patch.categoryId) {
          const cat = categories.find((c) => c.id === patch.categoryId);
          if (cat) updated.category = cat;
        }
        return updated;
      }) : prev,
    );
    updateMutation.mutate({ id, ...patch });
  }

  const columnDefs = useMemo((): ColDef<Transaction>[] => [
    {
      width: 40,
      sortable: false,
      filter: false,
      floatingFilter: false,
      resizable: false,
      suppressHeaderMenuButton: true,
    },
    {
      headerName: "DATE",
      valueGetter: (p) => p.data ? new Date(p.data.date) : null,
      valueFormatter: (p) => p.value ? (p.value as Date).toISOString().slice(0, 10) : "",
      width: 120,
      sort: "desc",
      filter: "agDateColumnFilter",
      filterParams: {
        comparator: (filterDate: Date, cellValue: Date | null) => {
          if (!cellValue) return -1;
          const cell = new Date(cellValue);
          cell.setHours(0, 0, 0, 0);
          if (cell < filterDate) return -1;
          if (cell > filterDate) return 1;
          return 0;
        },
      },
      floatingFilter: true,
    },
    {
      field: "description",
      headerName: "DESCRIPTION",
      flex: 3,
      filter: "agTextColumnFilter",
      floatingFilter: true,
    },
    {
      colId: "note",
      headerName: "NOTE",
      flex: 2,
      cellRenderer: NoteRenderer,
      valueGetter: (p) => p.data?.note ?? "",
      filter: "agTextColumnFilter",
      floatingFilter: true,
      sortable: false,
      cellStyle: { overflow: "visible" },
    },
    {
      headerName: "CATEGORY",
      flex: 1.5,
      cellRenderer: CategoryRenderer,
      valueGetter: (p) => p.data?.category?.name ?? "",
      filter: CategoryFilter,
      filterParams: { categories } as unknown as Record<string, unknown>,
      floatingFilter: false,
      cellStyle: { overflow: "visible" },
    },
    {
      field: "owner",
      headerName: "OWNER",
      width: 110,
      cellRenderer: OwnerRenderer,
      filter: OwnerFilter,
      floatingFilter: false,
      cellStyle: { overflow: "visible" },
    },
    {
      headerName: "SOURCE",
      width: 120,
      cellRenderer: SourceRenderer,
      valueGetter: (p) => p.data ? bankSource(p.data.externalId) : "",
      filter: SourceFilter,
      floatingFilter: false,
    },
    {
      headerName: "AMOUNT",
      width: 140,
      cellRenderer: AmountRenderer,
      // Sort by signed value, filter by absolute numeric value
      comparator: (_, __, nodeA, nodeB) => {
        const sign = (t: Transaction) => (t.type === "Income" ? 1 : -1) * Math.abs(parseFloat(t.amount));
        return sign(nodeA.data!) - sign(nodeB.data!);
      },
      filterValueGetter: (p) => p.data ? Math.abs(parseFloat(p.data.amount)) : null,
      filter: "agNumberColumnFilter",
      floatingFilter: true,
    },
    {
      headerName: "✓",
      width: 60,
      cellRenderer: ReviewedRenderer,
      valueGetter: (p) => p.data?.reviewed ?? false,
      sortable: true,
      filter: ReviewedFilter,
      floatingFilter: false,
      resizable: false,
      cellStyle: { display: "flex", alignItems: "center", justifyContent: "center" },
    },
  ], [categories]);

  const gridContext = useMemo((): GridCtx => ({
    update: (id, patch) => {
      // Immediately update the grid cell — don't wait for server
      const node = gridApiRef.current?.getRowNode(id);
      if (node?.data) {
        const updated: Transaction = { ...node.data, ...patch };
        if (patch.categoryId) {
          const cat = categories.find((c) => c.id === patch.categoryId);
          if (cat) updated.category = cat;
        }
        gridApiRef.current?.applyTransaction({ update: [updated] });
      }
      updateMutation.mutate({ id, ...patch });
    },
    categories,
    registerNoteEdit,
    unregisterNoteEdit,
  }), [updateMutation, categories, registerNoteEdit, unregisterNoteEdit]);

  return (
    <div className="max-w-[1400px] mx-auto space-y-8">
      <header className="rise rise-1 flex items-end justify-between gap-6">
        <div>
          <p className="eyebrow mb-3">{new Date().toLocaleDateString("en-GB", { month: "long", year: "numeric" })}</p>
          <h1 className="font-display text-[44px] leading-[1.02] font-light tracking-tight text-foreground">
            <span className="italic text-primary">Transactions</span>
          </h1>
          <p className="text-sm text-muted-foreground mt-2 max-w-xl">
            Categorise transactions, mark them reviewed, and see what we owe each other.
          </p>
        </div>
        <div className="hidden md:block text-right">
          <p className="eyebrow mb-1">Transactions</p>
          <p className="font-display text-3xl font-light text-foreground">
            {transactions.length.toLocaleString("en-GB")}
          </p>
        </div>
      </header>

      {/* Summary cards */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <Card className="surface-card-hover rise rise-2">
          <CardContent className="pt-6">
            <div className="flex items-baseline justify-between mb-3">
              <p className="eyebrow">Casey Monzo Sauce</p>
              <span className="size-1.5 rounded-full bg-[var(--signal)]" aria-hidden />
            </div>
            <p className="font-display text-[40px] leading-none font-light text-[var(--signal)]">
              {summary ? fmt(summary.caseyIn) : "—"}
            </p>
            <p className="text-xs text-muted-foreground mt-3 font-numeric tracking-tight">
              INCOMING THIS MONTH
            </p>
          </CardContent>
        </Card>

        <Card className="surface-card-hover rise rise-3">
          <CardContent className="pt-6">
            <div className="flex items-baseline justify-between mb-3">
              <p className="eyebrow">Joint Expenses</p>
              <span className="size-1.5 rounded-full bg-destructive/80" aria-hidden />
            </div>
            <p className="font-display text-[40px] leading-none font-light text-destructive/80">
              {summary ? fmt(summary.jointExpenses) : "—"}
            </p>
            <p className="text-xs text-muted-foreground mt-3 font-numeric tracking-tight">
              SHARED THIS MONTH
            </p>
          </CardContent>
        </Card>

        <Card className="surface-card-hover rise rise-4 relative overflow-hidden">
          <div
            className="absolute inset-0 pointer-events-none opacity-40"
            style={{
              background: "radial-gradient(120% 80% at 100% 0%, color-mix(in oklab, var(--primary) 22%, transparent), transparent 60%)",
            }}
            aria-hidden
          />
          <CardContent className="pt-6 relative">
            <div className="flex items-baseline justify-between mb-3">
              <p className="eyebrow">Settlement</p>
              <span className="size-1.5 rounded-full bg-primary" aria-hidden />
            </div>
            {summary ? (
              summary.settlement < -0.005 ? (
                <>
                  <p className="font-display text-[40px] leading-none font-light text-primary">
                    {fmt(Math.abs(summary.settlement))}
                  </p>
                  <p className="text-xs text-muted-foreground mt-3 font-numeric tracking-tight">CASEY OWES POT</p>
                </>
              ) : summary.settlement > 0.005 ? (
                <>
                  <p className="font-display text-[40px] leading-none font-light text-primary">
                    {fmt(summary.settlement)}
                  </p>
                  <p className="text-xs text-muted-foreground mt-3 font-numeric tracking-tight">ALEX OWES CASEY</p>
                </>
              ) : (
                <>
                  <p className="font-display text-[40px] leading-none font-light italic text-primary">
                    All square
                  </p>
                  <p className="text-xs text-muted-foreground mt-3 font-numeric tracking-tight">NOTHING TO SETTLE</p>
                </>
              )
            ) : <p className="font-display text-[40px] font-light text-muted-foreground">—</p>}
          </CardContent>
        </Card>
      </div>

      {/* Add transaction */}
      <Card className="rise rise-5">
        <CardHeader>
          <CardTitle className="eyebrow">Add Transaction</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="flex flex-wrap gap-2">
            <Input
              placeholder="Description"
              value={form.description}
              onChange={(e) => setForm((f) => ({ ...f, description: e.target.value }))}
              className="flex-1 min-w-[200px] h-10 bg-background/40"
            />
            <Input
              placeholder="0.00"
              type="number"
              min="0"
              step="0.01"
              value={form.amount}
              onChange={(e) => setForm((f) => ({ ...f, amount: e.target.value }))}
              className="w-32 h-10 font-numeric bg-background/40"
            />
            <select
              value={form.type}
              onChange={(e) => setForm((f) => ({ ...f, type: e.target.value as "Income" | "Expense" }))}
              className="border border-input bg-background/40 text-foreground rounded-md px-3 h-10 text-sm"
            >
              <option value="Expense">Expense</option>
              <option value="Income">Income</option>
            </select>
            <select
              value={form.categoryId}
              onChange={(e) => setForm((f) => ({ ...f, categoryId: e.target.value }))}
              className="border border-input bg-background/40 text-foreground rounded-md px-3 h-10 text-sm"
            >
              {categories.map((c) => (
                <option key={c.id} value={c.id}>{c.name}</option>
              ))}
            </select>
            <Button onClick={handleAdd} disabled={addMutation.isPending} className="h-10 px-5">
              {addMutation.isPending ? "Adding…" : "Add"}
            </Button>
          </div>
        </CardContent>
      </Card>

      {/* Transactions — mobile card list */}
      <Card className="rise rise-6 overflow-hidden md:hidden">
        <CardHeader className="pb-2">
          <div className="flex items-baseline justify-between gap-3">
            <div className="flex items-baseline gap-3">
              <CardTitle className="eyebrow">Transactions</CardTitle>
              <span className="text-xs text-muted-foreground font-numeric">
                {mobileFiltersActive
                  ? <>{mobileTransactions.length} <span className="text-muted-foreground/50">of</span> {transactions.length}</>
                  : <>{transactions.length} entries</>}
              </span>
            </div>
            {mobileFiltersActive && (
              <button
                onClick={resetMobileFilters}
                className="text-xs text-muted-foreground hover:text-foreground underline-offset-2 hover:underline"
              >
                Clear
              </button>
            )}
          </div>
        </CardHeader>
        <CardContent className="p-0">
          <div className="px-4 pb-3 space-y-2">
            <Input
              placeholder="Search description or note…"
              value={mobileSearch}
              onChange={(e) => setMobileSearch(e.target.value)}
              className="h-9 bg-background/40 text-sm"
            />
            <div className="flex items-center gap-1.5 overflow-x-auto -mx-1 px-1 pb-0.5">
              {(["All", "Alex", "Casey", "Joint"] as const).map((o) => (
                <button
                  key={o}
                  onClick={() => setMobileOwner(o)}
                  className={`shrink-0 px-2.5 py-1 rounded-full text-xs font-medium border transition-colors ${
                    mobileOwner === o
                      ? o === "All"
                        ? "bg-foreground text-background border-foreground"
                        : `${OWNER_STYLES[o as Owner]} bg-accent/50`
                      : o === "All"
                        ? "text-muted-foreground border-border hover:bg-accent"
                        : `${OWNER_STYLES[o as Owner]} opacity-60 hover:opacity-100`
                  }`}
                >
                  {o}
                </button>
              ))}
            </div>
            <div className="flex items-center gap-1.5 overflow-x-auto -mx-1 px-1 pb-0.5">
              {(["All", "Unreviewed", "Reviewed"] as const).map((s) => (
                <button
                  key={s}
                  onClick={() => setMobileStatus(s)}
                  className={`shrink-0 px-2.5 py-1 rounded-full text-xs font-medium border transition-colors ${
                    mobileStatus === s
                      ? s === "Reviewed"
                        ? "text-green-600 border-green-600 bg-green-600/10"
                        : s === "Unreviewed"
                          ? "text-amber-600 border-amber-600 bg-amber-600/10"
                          : "bg-foreground text-background border-foreground"
                      : "text-muted-foreground border-border hover:bg-accent"
                  }`}
                >
                  {s}
                </button>
              ))}
              <button
                onClick={() => setMobileFiltersOpen((v) => !v)}
                className={`shrink-0 ml-auto px-2.5 py-1 rounded-full text-xs font-medium border transition-colors ${
                  mobileCategoryId !== "All" || mobileSource !== "All"
                    ? "bg-primary text-primary-foreground border-primary"
                    : "text-muted-foreground border-border hover:bg-accent"
                }`}
              >
                {mobileFiltersOpen ? "Less" : "More"}
              </button>
            </div>
            {mobileFiltersOpen && (
              <div className="flex items-center gap-2 pt-1">
                <select
                  value={mobileCategoryId}
                  onChange={(e) => setMobileCategoryId(e.target.value)}
                  className="flex-1 border border-input bg-background/40 text-foreground rounded-md px-2 h-9 text-xs"
                >
                  <option value="All">All categories</option>
                  {categories.map((c) => (
                    <option key={c.id} value={c.id}>{c.name}</option>
                  ))}
                </select>
                <select
                  value={mobileSource}
                  onChange={(e) => setMobileSource(e.target.value as "All" | BankSource)}
                  className="flex-1 border border-input bg-background/40 text-foreground rounded-md px-2 h-9 text-xs"
                >
                  <option value="All">All sources</option>
                  {SOURCES.map((s) => (
                    <option key={s} value={s}>{s}</option>
                  ))}
                </select>
              </div>
            )}
          </div>
          <div className="divide-y divide-border px-4">
            {mobileTransactions.length === 0 ? (
              <div className="py-8 text-center text-sm text-muted-foreground">
                No transactions match these filters.
              </div>
            ) : mobileTransactions.map((tx) => (
              <MobileTransactionCard
                key={tx.id}
                tx={tx}
                categories={categories}
                onUpdate={handleMobileUpdate}
              />
            ))}
          </div>
        </CardContent>
      </Card>

      {/* Transactions — desktop AG Grid */}
      <Card className="rise rise-6 overflow-hidden hidden md:block">
        <CardHeader className="pb-3 flex flex-row items-center justify-between">
          <div className="flex items-baseline gap-3">
            <CardTitle className="eyebrow">Transactions</CardTitle>
            <span className="text-xs text-muted-foreground font-numeric">
              {filteredCount !== null
                ? <>{filteredCount} <span className="text-muted-foreground/50">of</span> {transactions.length}</>
                : <>{transactions.length} entries</>}
            </span>
            {filteredSum !== null && (
              <span className="text-xs text-muted-foreground">
                · Net <span className={`font-numeric font-semibold ${filteredSum >= 0 ? "text-[var(--signal)]" : "text-destructive"}`}>{filteredSum >= 0 ? "+" : ""}{fmt(filteredSum)}</span>
              </span>
            )}
          </div>
          <div className="flex items-center gap-2">
            {selectedIds.length > 0 ? (
              <>
                <Button
                  variant="outline"
                  size="sm"
                  className="text-xs text-green-600 border-green-600/40 hover:bg-green-600/10"
                  disabled={bulkReviewMutation.isPending}
                  onClick={() => handleBulkReview(true)}
                >
                  Mark {selectedIds.length} reviewed
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  className="text-xs text-muted-foreground hover:text-foreground"
                  disabled={bulkReviewMutation.isPending}
                  onClick={() => handleBulkReview(false)}
                >
                  Unmark
                </Button>
              </>
            ) : hasFilters && (
              <Button
                variant="outline"
                size="sm"
                className="text-xs text-green-600 border-green-600/40 hover:bg-green-600/10"
                disabled={bulkReviewMutation.isPending}
                onClick={() => handleBulkReview(true)}
              >
                Mark filtered reviewed
              </Button>
            )}
            <Button
              variant="outline"
              size="sm"
              className="text-xs text-muted-foreground hover:text-foreground"
              disabled={!hasFilters}
              onClick={() => { gridApiRef.current?.setFilterModel(null); }}
            >
              Clear filters
            </Button>
          </div>
        </CardHeader>
        <CardContent className="p-0">
          <AgGridReact<Transaction>
            theme={gridTheme}
            rowData={transactions}
            columnDefs={columnDefs}
            context={gridContext}
            domLayout="autoHeight"
            defaultColDef={{ sortable: true, resizable: true, suppressMovable: true }}
            rowSelection={{ mode: "multiRow", checkboxes: true, headerCheckbox: true, enableClickSelection: false }}
            enableCellTextSelection
            getRowId={(p) => p.data.id}
            onGridReady={(e) => { gridApiRef.current = e.api; }}
            onCellKeyDown={(params) => {
              if (params.event?.key !== "Enter" || params.column.getColId() !== "note" || !params.data) return;
              params.event.preventDefault();
              noteEditTriggers.current.get(params.data.id)?.();
            }}
            onSelectionChanged={(e) => {
              setSelectedIds(e.api.getSelectedRows().map((r) => r.id));
            }}
            onFilterChanged={(e) => {
              const model = e.api.getFilterModel();
              const active = Object.keys(model).length > 0;
              setHasFilters(active);
              if (!active) { setFilteredCount(null); setFilteredSum(null); return; }
              let sum = 0;
              e.api.forEachNodeAfterFilter((node) => {
                const tx = node.data as Transaction | undefined;
                if (!tx) return;
                sum += tx.type === "Income" ? parseFloat(tx.amount) : -parseFloat(tx.amount);
              });
              setFilteredCount(e.api.getDisplayedRowCount());
              setFilteredSum(sum);
            }}
          />
        </CardContent>
      </Card>

    </div>
  );
}
