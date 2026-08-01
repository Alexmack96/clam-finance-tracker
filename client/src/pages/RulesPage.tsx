import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ClientSideRowModelModule,
  CellStyleModule,
  ValidationModule,
  ModuleRegistry,
  themeQuartz,
} from "ag-grid-community";
import type { ColDef } from "ag-grid-community";
import { AgGridReact } from "ag-grid-react";
import {
  ArrowDown,
  ArrowUp,
  Ban,
  Pencil,
  Play,
  Plus,
  Trash2,
  TriangleAlert,
  X,
} from "lucide-react";
import {
  BUCKETS,
  KNOWN_BANKS,
  RULE_FIELDS,
  RULE_FIELD_LABELS,
  RULE_OPERATORS,
  RULE_OPERATOR_LABELS,
  type Bucket,
  type Category,
  type KnownBank,
  type Rule,
  type RuleConditionInput,
  type RuleField,
  type RuleJoin,
  type RuleKind,
  type RuleOperator,
  type RulePreview,
} from "@clam/core";
import api from "../lib/api.js";
import { Card, CardContent, CardHeader, CardTitle } from "../components/ui/card.js";
import { Button } from "../components/ui/button.js";
import { Input } from "../components/ui/input.js";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "../components/ui/alert-dialog.js";

ModuleRegistry.registerModules([ClientSideRowModelModule, CellStyleModule, ValidationModule]);

const gridTheme = themeQuartz.withParams({
  backgroundColor: "var(--card)",
  foregroundColor: "var(--foreground)",
  borderColor: "var(--border)",
  headerBackgroundColor: "var(--card)",
  headerTextColor: "var(--muted-foreground)",
  subtleTextColor: "var(--muted-foreground)",
  accentColor: "var(--primary)",
  rowHoverColor: "var(--accent)",
  fontFamily: "inherit",
  fontSize: 12,
  headerHeight: 32,
  rowHeight: 34,
  wrapperBorder: false,
  wrapperBorderRadius: 0,
  cellHorizontalPadding: 10,
  browserColorScheme: "inherit",
});

const BANK_LABELS: Record<KnownBank, string> = {
  monzo: "Monzo",
  flex: "Flex",
  amex: "Amex",
  barclays: "Barclays",
  santander: "Santander",
  hsbc: "HSBC",
  sofi: "SoFi",
  chase: "Chase",
};

const BUCKET_STYLES: Record<Bucket, string> = {
  Needs: "text-sky-700 dark:text-sky-300 border-sky-500/40",
  Wants: "text-amber-700 dark:text-amber-300 border-amber-500/40",
  Savings: "text-emerald-700 dark:text-emerald-300 border-emerald-500/40",
  Ignore: "text-muted-foreground border-muted-foreground/40",
};

function apiError(err: unknown): string {
  const e = err as { response?: { data?: { error?: string; errors?: Record<string, string[]> } } };
  const data = e.response?.data;
  if (data?.error) return data.error;
  const first = data?.errors && Object.values(data.errors)[0]?.[0];
  return first ?? "Something went wrong";
}

type DraftCondition = RuleConditionInput;

type Draft = {
  kind: RuleKind;
  joinOperator: RuleJoin;
  bank: KnownBank | null;
  categoryId: string | null;
  bucket: Bucket | null;
  conditions: DraftCondition[];
};

function emptyDraft(kind: RuleKind, categoryId: string | null): Draft {
  return {
    kind,
    joinOperator: "AND",
    bank: null,
    categoryId: kind === "Category" ? categoryId : null,
    bucket: kind === "Bucket" ? "Needs" : null,
    conditions: [{ field: "Description", operator: "Contains", value: "", negate: false }],
  };
}

function draftFromRule(rule: Rule): Draft {
  return {
    kind: rule.kind,
    joinOperator: rule.joinOperator,
    bank: rule.bank,
    categoryId: rule.categoryId,
    bucket: rule.bucket,
    conditions: rule.conditions.map((c) => ({
      field: c.field,
      operator: c.operator,
      value: c.value,
      negate: c.negate,
    })),
  };
}

export default function RulesPage() {
  const { data: rules, isLoading } = useQuery<Rule[]>({
    queryKey: ["rules"],
    queryFn: () => api.get("/api/rules").then((r) => r.data),
  });
  const { data: categories } = useQuery<Category[]>({
    queryKey: ["categories"],
    queryFn: () => api.get("/api/categories").then((r) => r.data),
  });

  const categoryRules = useMemo(
    () =>
      (rules ?? []).filter((r) => r.kind === "Category").sort((a, b) => a.position - b.position),
    [rules],
  );
  const bucketRules = useMemo(
    () => (rules ?? []).filter((r) => r.kind === "Bucket").sort((a, b) => a.position - b.position),
    [rules],
  );

  const [preview, setPreview] = useState<{ preview: RulePreview; scope: PreviewScope } | null>(
    null,
  );

  return (
    <div className="max-w-6xl mx-auto px-4 py-6 space-y-6">
      <div>
        <h1 className="text-2xl font-semibold">Rules</h1>
        <p className="text-sm text-muted-foreground mt-1">
          Rules run top to bottom, first match wins — the order you see is the order that runs.
          Category rules resolve first, then Bucket rules, so a Bucket rule can test the Category a
          Category rule just assigned.
        </p>
      </div>

      <RunAllCard onPreview={(p) => setPreview({ preview: p, scope: { scope: "all" } })} />

      {isLoading ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : (
        <>
          <RuleSection
            kind="Category"
            title="Category rules"
            blurb="Match a transaction's description and assign a category."
            rules={categoryRules}
            allRules={rules ?? []}
            categories={categories ?? []}
            onPreview={(p, scope) => setPreview({ preview: p, scope })}
          />
          <RuleSection
            kind="Bucket"
            title="Bucket rules"
            blurb="Assign the 50/30/20 bucket. These can test Category, so “Transport + uber → Wants” works."
            rules={bucketRules}
            allRules={rules ?? []}
            categories={categories ?? []}
            onPreview={(p, scope) => setPreview({ preview: p, scope })}
          />
        </>
      )}

      {preview && (
        <PreviewDialog
          preview={preview.preview}
          scope={preview.scope}
          rules={rules ?? []}
          onClose={() => setPreview(null)}
        />
      )}
    </div>
  );
}

// ─── Run all ─────────────────────────────────────────────────────────────────

type PreviewScope =
  | { scope: "all" }
  | { scope: "rule"; ruleId: string }
  | { scope: "draft"; ruleId?: string };

function RunAllCard({ onPreview }: { onPreview: (p: RulePreview) => void }) {
  const mutation = useMutation({
    mutationFn: () =>
      api.post("/api/rules/preview", { scope: "all" }).then((r) => r.data as RulePreview),
    onSuccess: onPreview,
  });

  return (
    <Card>
      <CardContent className="flex flex-col sm:flex-row sm:items-center gap-3 py-4">
        <div className="flex-1">
          <p className="text-sm font-medium">Run all rules</p>
          <p className="text-xs text-muted-foreground">
            Evaluates every rule over every transaction. You see exactly what changes before
            anything is written — pinned fields are never touched.
          </p>
        </div>
        <Button
          onClick={() => mutation.mutate()}
          disabled={mutation.isPending}
          className="shrink-0"
        >
          <Play className="h-4 w-4" />
          {mutation.isPending ? "Checking…" : "Dry run"}
        </Button>
      </CardContent>
      {mutation.isError && (
        <CardContent className="pt-0">
          <p className="text-xs text-destructive">{apiError(mutation.error)}</p>
        </CardContent>
      )}
    </Card>
  );
}

// ─── One ordered list ────────────────────────────────────────────────────────

function RuleSection({
  kind,
  title,
  blurb,
  rules,
  allRules,
  categories,
  onPreview,
}: {
  kind: RuleKind;
  title: string;
  blurb: string;
  rules: Rule[];
  allRules: Rule[];
  categories: Category[];
  onPreview: (p: RulePreview, scope: PreviewScope) => void;
}) {
  const queryClient = useQueryClient();
  const [editingId, setEditingId] = useState<string | null>(null);
  const [draft, setDraft] = useState<Draft | null>(null);

  const categoryById = useMemo(
    () => new Map(categories.map((c) => [c.id, c] as const)),
    [categories],
  );

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ["rules"] });
  };

  const reorder = useMutation({
    mutationFn: (ids: string[]) => api.post("/api/rules/reorder", { kind, ids }),
    onSuccess: invalidate,
  });

  const remove = useMutation({
    mutationFn: (id: string) => api.delete(`/api/rules/${id}`),
    onSuccess: invalidate,
  });

  const previewRule = useMutation({
    mutationFn: (ruleId: string) =>
      api.post("/api/rules/preview", { scope: "rule", ruleId }).then((r) => r.data as RulePreview),
    onSuccess: (p, ruleId) => onPreview(p, { scope: "rule", ruleId }),
  });

  function move(index: number, delta: number) {
    const next = [...rules];
    const target = index + delta;
    if (target < 0 || target >= next.length) return;
    const [row] = next.splice(index, 1);
    if (row) next.splice(target, 0, row);
    reorder.mutate(next.map((r) => r.id));
  }

  function startCreate() {
    setEditingId(null);
    setDraft(emptyDraft(kind, categories[0]?.id ?? null));
  }

  return (
    <Card>
      <CardHeader className="pb-3">
        <div className="flex items-start justify-between gap-3">
          <div>
            <CardTitle className="text-base">{title}</CardTitle>
            <p className="text-xs text-muted-foreground mt-1">{blurb}</p>
          </div>
          <Button size="sm" variant="outline" onClick={startCreate} className="shrink-0">
            <Plus className="h-4 w-4" />
            Add rule
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-2">
        {rules.length === 0 && !draft && (
          <p className="text-xs text-muted-foreground">No rules yet.</p>
        )}

        {rules.map((rule, i) =>
          editingId === rule.id && draft ? (
            <RuleEditor
              key={rule.id}
              draft={draft}
              setDraft={setDraft}
              categories={categories}
              ruleId={rule.id}
              onDone={() => {
                setDraft(null);
                setEditingId(null);
              }}
              onPreview={onPreview}
            />
          ) : (
            <div
              key={rule.id}
              className="flex items-center gap-2 rounded-md border border-input bg-background px-2 py-1.5"
            >
              <span className="w-6 shrink-0 text-center text-[10px] font-mono text-muted-foreground">
                {i + 1}
              </span>
              <div className="flex flex-col shrink-0">
                <button
                  className="text-muted-foreground hover:text-foreground disabled:opacity-30"
                  disabled={i === 0 || reorder.isPending}
                  onClick={() => move(i, -1)}
                  title="Move up"
                >
                  <ArrowUp className="h-3 w-3" />
                </button>
                <button
                  className="text-muted-foreground hover:text-foreground disabled:opacity-30"
                  disabled={i === rules.length - 1 || reorder.isPending}
                  onClick={() => move(i, 1)}
                  title="Move down"
                >
                  <ArrowDown className="h-3 w-3" />
                </button>
              </div>

              <div className="flex-1 min-w-0 flex flex-wrap items-center gap-1.5">
                <ConditionSummary rule={rule} />
                <span className="text-xs text-muted-foreground">→</span>
                {rule.kind === "Category" ? (
                  <span className="text-xs font-medium">
                    {rule.categoryId ? (categoryById.get(rule.categoryId)?.name ?? "—") : "—"}
                  </span>
                ) : (
                  <span
                    className={`px-1.5 py-0.5 rounded-full text-[10px] font-medium border ${rule.bucket ? BUCKET_STYLES[rule.bucket] : ""}`}
                  >
                    {rule.bucket ?? "—"}
                  </span>
                )}
                {rule.bank && (
                  <span className="px-1.5 py-0.5 rounded-full text-[10px] border text-muted-foreground border-muted-foreground/40">
                    {BANK_LABELS[rule.bank]}
                  </span>
                )}
              </div>

              <div className="flex items-center gap-1 shrink-0">
                <Button
                  variant="ghost"
                  size="icon"
                  className="h-6 w-6"
                  title="Dry run this rule"
                  disabled={previewRule.isPending}
                  onClick={() => previewRule.mutate(rule.id)}
                >
                  <Play className="h-3 w-3" />
                </Button>
                <Button
                  variant="ghost"
                  size="icon"
                  className="h-6 w-6"
                  title="Edit"
                  onClick={() => {
                    setEditingId(rule.id);
                    setDraft(draftFromRule(rule));
                  }}
                >
                  <Pencil className="h-3 w-3" />
                </Button>
                <Button
                  variant="ghost"
                  size="icon"
                  className="h-6 w-6 text-destructive hover:text-destructive"
                  title="Delete"
                  onClick={() => remove.mutate(rule.id)}
                >
                  <Trash2 className="h-3 w-3" />
                </Button>
              </div>
            </div>
          ),
        )}

        {draft && editingId === null && (
          <RuleEditor
            draft={draft}
            setDraft={setDraft}
            categories={categories}
            onDone={() => setDraft(null)}
            onPreview={onPreview}
          />
        )}

        {previewRule.isError && (
          <p className="text-xs text-destructive">{apiError(previewRule.error)}</p>
        )}
        {allRules.length === 0 && null}
      </CardContent>
    </Card>
  );
}

function ConditionSummary({ rule }: { rule: Rule }) {
  const positives = rule.conditions.filter((c) => !c.negate);
  const negatives = rule.conditions.filter((c) => c.negate);
  return (
    <span className="flex flex-wrap items-center gap-1 min-w-0">
      {positives.map((c, i) => (
        <span key={c.id} className="flex items-center gap-1">
          {i > 0 && (
            <span className="text-[10px] font-semibold text-muted-foreground">
              {rule.joinOperator}
            </span>
          )}
          <span className="font-mono text-[11px] bg-muted rounded px-1.5 py-0.5 truncate max-w-[18rem]">
            {RULE_FIELD_LABELS[c.field]} {RULE_OPERATOR_LABELS[c.operator]} “{c.value}”
          </span>
        </span>
      ))}
      {negatives.map((c) => (
        <span key={c.id} className="flex items-center gap-1">
          <span className="text-[10px] font-semibold text-muted-foreground">EXCEPT</span>
          <span className="font-mono text-[11px] bg-destructive/10 text-destructive rounded px-1.5 py-0.5 truncate max-w-[18rem]">
            {RULE_FIELD_LABELS[c.field]} {RULE_OPERATOR_LABELS[c.operator]} “{c.value}”
          </span>
        </span>
      ))}
    </span>
  );
}

// ─── Editor ──────────────────────────────────────────────────────────────────

function RuleEditor({
  draft,
  setDraft,
  categories,
  ruleId,
  onDone,
  onPreview,
}: {
  draft: Draft;
  setDraft: (d: Draft) => void;
  categories: Category[];
  ruleId?: string;
  onDone: () => void;
  onPreview: (p: RulePreview, scope: PreviewScope) => void;
}) {
  const queryClient = useQueryClient();

  const save = useMutation({
    mutationFn: () =>
      ruleId ? api.patch(`/api/rules/${ruleId}`, draft) : api.post("/api/rules", draft),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["rules"] });
      onDone();
    },
  });

  const previewDraft = useMutation({
    mutationFn: () =>
      api
        .post("/api/rules/preview", { scope: "draft", rule: draft, ...(ruleId ? { ruleId } : {}) })
        .then((r) => r.data as RulePreview),
    onSuccess: (p) => onPreview(p, { scope: "draft", ruleId }),
  });

  function setCondition(i: number, patch: Partial<DraftCondition>) {
    const conditions = draft.conditions.map((c, idx) => (idx === i ? { ...c, ...patch } : c));
    setDraft({ ...draft, conditions });
  }

  const positives = draft.conditions.filter((c) => !c.negate).length;
  const selectClass =
    "h-8 rounded-md border border-input bg-background px-2 text-xs shrink-0 min-w-0";

  return (
    <div className="rounded-md border border-primary/40 bg-muted/30 p-3 space-y-3">
      <div className="space-y-2">
        {draft.conditions.map((c, i) => (
          <div key={i} className="flex flex-wrap items-center gap-2">
            <span className="w-14 shrink-0 text-[10px] font-semibold text-muted-foreground">
              {i === 0 ? "WHERE" : c.negate ? "EXCEPT" : draft.joinOperator}
            </span>
            <select
              value={c.field}
              onChange={(e) => setCondition(i, { field: e.target.value as RuleField })}
              className={selectClass}
            >
              {RULE_FIELDS.filter((f) => draft.kind === "Bucket" || f !== "Category").map((f) => (
                <option key={f} value={f}>
                  {RULE_FIELD_LABELS[f]}
                </option>
              ))}
            </select>
            <select
              value={c.operator}
              onChange={(e) => setCondition(i, { operator: e.target.value as RuleOperator })}
              className={selectClass}
            >
              {RULE_OPERATORS.map((op) => (
                <option key={op} value={op}>
                  {RULE_OPERATOR_LABELS[op]}
                </option>
              ))}
            </select>
            <Input
              value={c.value}
              onChange={(e) => setCondition(i, { value: e.target.value })}
              placeholder="value"
              className="h-8 text-xs flex-1 min-w-[10rem]"
            />
            <Button
              type="button"
              variant={c.negate ? "default" : "outline"}
              size="sm"
              className="h-8 shrink-0"
              title={c.negate ? "Exclusion — click to make it a match" : "Turn into an exclusion"}
              onClick={() => setCondition(i, { negate: !c.negate })}
            >
              <Ban className="h-3 w-3" />
              not
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="h-8 w-8 shrink-0"
              disabled={draft.conditions.length === 1}
              onClick={() =>
                setDraft({ ...draft, conditions: draft.conditions.filter((_, idx) => idx !== i) })
              }
            >
              <X className="h-3 w-3" />
            </Button>
          </div>
        ))}
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="h-8"
          onClick={() =>
            setDraft({
              ...draft,
              conditions: [
                ...draft.conditions,
                { field: "Description", operator: "Contains", value: "", negate: false },
              ],
            })
          }
        >
          <Plus className="h-3 w-3" />
          Condition
        </Button>

        <label className="flex items-center gap-1.5 text-xs">
          <span className="text-muted-foreground">Match</span>
          <select
            value={draft.joinOperator}
            onChange={(e) => setDraft({ ...draft, joinOperator: e.target.value as RuleJoin })}
            className={selectClass}
          >
            <option value="AND">all conditions</option>
            <option value="OR">any condition</option>
          </select>
        </label>

        <select
          value={draft.bank ?? ""}
          onChange={(e) =>
            setDraft({
              ...draft,
              bank: e.target.value === "" ? null : (e.target.value as KnownBank),
            })
          }
          className={selectClass}
        >
          <option value="">Any bank</option>
          {KNOWN_BANKS.map((b) => (
            <option key={b} value={b}>
              {BANK_LABELS[b]}
            </option>
          ))}
        </select>

        <span className="text-xs text-muted-foreground">→</span>

        {draft.kind === "Category" ? (
          <select
            value={draft.categoryId ?? ""}
            onChange={(e) => setDraft({ ...draft, categoryId: e.target.value || null })}
            className={selectClass}
          >
            {categories.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        ) : (
          <select
            value={draft.bucket ?? ""}
            onChange={(e) => setDraft({ ...draft, bucket: e.target.value as Bucket })}
            className={selectClass}
          >
            {BUCKETS.map((b) => (
              <option key={b} value={b}>
                {b}
              </option>
            ))}
          </select>
        )}
      </div>

      {positives === 0 && (
        <p className="flex items-center gap-1.5 text-xs text-amber-600 dark:text-amber-400">
          <TriangleAlert className="h-3 w-3" />A rule needs at least one condition that isn’t an
          exclusion — otherwise it matches almost everything.
        </p>
      )}

      <div className="flex items-center gap-2">
        <Button
          size="sm"
          className="h-8"
          disabled={save.isPending || positives === 0}
          onClick={() => save.mutate()}
        >
          {ruleId ? "Save" : "Add rule"}
        </Button>
        <Button
          size="sm"
          variant="outline"
          className="h-8"
          disabled={previewDraft.isPending || positives === 0}
          onClick={() => previewDraft.mutate()}
        >
          <Play className="h-3 w-3" />
          {previewDraft.isPending ? "Checking…" : "Dry run"}
        </Button>
        <Button size="sm" variant="ghost" className="h-8" onClick={onDone}>
          Cancel
        </Button>
      </div>

      {save.isError && <p className="text-xs text-destructive">{apiError(save.error)}</p>}
      {previewDraft.isError && (
        <p className="text-xs text-destructive">{apiError(previewDraft.error)}</p>
      )}
    </div>
  );
}

// ─── Dry-run result ──────────────────────────────────────────────────────────

function PreviewDialog({
  preview,
  scope,
  rules,
  onClose,
}: {
  preview: RulePreview;
  scope: PreviewScope;
  rules: Rule[];
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const ruleById = useMemo(() => new Map(rules.map((r) => [r.id, r] as const)), [rules]);

  const apply = useMutation({
    mutationFn: () =>
      api
        .post("/api/rules/apply", scope.scope === "rule" ? scope : { scope: "all" })
        .then(
          (r) => r.data as { categoryChanges: number; bucketChanges: number; affected: number },
        ),
    onSuccess: () => {
      for (const key of ["transactions", "categories", "summary", "analytics", "rules"]) {
        queryClient.invalidateQueries({ queryKey: [key] });
      }
      onClose();
    },
  });

  const columnDefs = useMemo<ColDef[]>(
    () => [
      {
        headerName: "Date",
        field: "date",
        width: 105,
        valueFormatter: (p) => (p.value ? new Date(p.value as string).toLocaleDateString() : ""),
      },
      { headerName: "Description", field: "description", flex: 2, minWidth: 200 },
      {
        headerName: "Amount",
        field: "amount",
        width: 105,
        valueFormatter: (p) => (typeof p.value === "number" ? p.value.toFixed(2) : ""),
        cellStyle: { textAlign: "right" },
      },
      { headerName: "Bank", field: "bank", width: 95 },
      { headerName: "Category", field: "currentCategory", width: 140 },
      {
        headerName: "→ Category",
        field: "proposedCategory",
        width: 140,
        cellStyle: (p) => (p.value ? { color: "var(--primary)", fontWeight: 600 } : undefined),
      },
      { headerName: "Bucket", field: "currentBucket", width: 110 },
      {
        headerName: "→ Bucket",
        field: "proposedBucket",
        width: 110,
        cellStyle: (p) => (p.value ? { color: "var(--primary)", fontWeight: 600 } : undefined),
      },
      {
        // Without this you can see a row is wrong but not which rule to edit —
        // with an ordered list and AND/OR conditions you will not guess.
        headerName: "Winning rule",
        width: 170,
        valueGetter: (p) => {
          const id = p.data.categoryRuleId ?? p.data.bucketRuleId;
          if (!id) return "";
          if (id === "__draft__") return "this draft";
          const rule = ruleById.get(id);
          if (!rule) return "—";
          const first = rule.conditions[0];
          return `#${rule.position + 1} ${first ? first.value : ""}`;
        },
      },
    ],
    [ruleById],
  );

  const title =
    scope.scope === "all"
      ? "Run all rules"
      : scope.scope === "draft"
        ? "Dry run — unsaved rule"
        : "Dry run — one rule";

  const canApply = scope.scope !== "draft";

  return (
    <AlertDialog open onOpenChange={(open) => !open && onClose()}>
      <AlertDialogContent className="max-w-[min(96vw,72rem)]">
        <AlertDialogHeader>
          <AlertDialogTitle>{title}</AlertDialogTitle>
          <AlertDialogDescription asChild>
            <div className="space-y-1">
              <p>
                Scanned {preview.scanned.toLocaleString()} transactions.{" "}
                <strong>{preview.rows.length.toLocaleString()}</strong> would change —{" "}
                {preview.categoryChanges.toLocaleString()} category,{" "}
                {preview.bucketChanges.toLocaleString()} bucket.
              </p>
              {preview.matched !== undefined && preview.won !== undefined && (
                <p>
                  This rule matches <strong>{preview.matched.toLocaleString()}</strong> transactions
                  and wins <strong>{preview.won.toLocaleString()}</strong> of them; the rest are
                  claimed by a rule above it.
                  {preview.matched > 0 && preview.won === 0 && (
                    <> Nothing will change — move it up the list if it should take precedence.</>
                  )}
                </p>
              )}
              {preview.pinnedSkipped > 0 && (
                <p className="text-amber-600 dark:text-amber-400">
                  {preview.pinnedSkipped.toLocaleString()} transaction
                  {preview.pinnedSkipped === 1 ? " was" : "s were"} left alone because the field was
                  pinned by a hand edit.
                </p>
              )}
            </div>
          </AlertDialogDescription>
        </AlertDialogHeader>

        <div className="h-[55vh] overflow-hidden border border-border rounded-md">
          {preview.rows.length === 0 ? (
            <p className="p-4 text-sm text-muted-foreground">Nothing would change.</p>
          ) : (
            <AgGridReact
              theme={gridTheme}
              rowData={preview.rows}
              columnDefs={columnDefs}
              defaultColDef={{ sortable: true, resizable: true, suppressMovable: true }}
              getRowId={(p) => p.data.id}
            />
          )}
        </div>

        {apply.isError && <p className="text-xs text-destructive">{apiError(apply.error)}</p>}

        <AlertDialogFooter>
          <AlertDialogCancel onClick={onClose}>Cancel</AlertDialogCancel>
          {canApply && (
            <AlertDialogAction
              onClick={(e) => {
                e.preventDefault();
                apply.mutate();
              }}
              disabled={apply.isPending || preview.rows.length === 0}
            >
              {apply.isPending
                ? "Applying…"
                : `Apply ${preview.rows.length.toLocaleString()} change${preview.rows.length === 1 ? "" : "s"}`}
            </AlertDialogAction>
          )}
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
