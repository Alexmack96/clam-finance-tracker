import { Fragment, useMemo, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { AxiosError } from "axios";
import { Pencil, Trash2, Plus, ChevronRight } from "lucide-react";
import {
  createCategorySchema,
  createCategoryRuleSchema,
  buckets,
  KNOWN_BANKS,
  type CreateCategoryInput,
  type CreateCategoryRuleInput,
  type Category,
  type CategoryRule,
  type KnownBank,
} from "@clam/core";
import api from "../lib/api.js";
import { SOURCE_STYLES, type BankSource } from "../lib/bankSource.js";
import { Card, CardContent } from "../components/ui/card.js";
import { Button } from "../components/ui/button.js";
import { Input } from "../components/ui/input.js";
import { Label } from "../components/ui/label.js";
import { Badge } from "../components/ui/badge.js";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "../components/ui/dialog.js";
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
import { Skeleton } from "../components/ui/skeleton.js";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "../components/ui/table.js";

type CategoryRow = Category & { transactionCount: number };
type RuleRow = CategoryRule;

const DEFAULT_COLOR = "#14b8a6";

const BANK_LABELS: Record<KnownBank, BankSource> = {
  monzo: "Monzo",
  flex: "Flex",
  amex: "Amex",
  barclays: "Barclays",
  santander: "Santander",
  hsbc: "HSBC",
  sofi: "SoFi",
  chase: "Chase",
};

// Pull a human-readable message out of an axios error, whatever shape it took.
function apiError(err: unknown): string {
  if (err instanceof AxiosError) {
    const data = err.response?.data;
    if (data?.error) return data.error;
    if (data?.errors) return Object.values(data.errors).flat().join(", ");
  }
  return "Something went wrong";
}

export function CategoriesPage() {
  const queryClient = useQueryClient();
  const [editorOpen, setEditorOpen] = useState(false);
  const [editing, setEditing] = useState<CategoryRow | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<CategoryRow | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());

  const { data: categories, isPending } = useQuery<CategoryRow[]>({
    queryKey: ["categories"],
    queryFn: () => api.get("/api/categories").then((r) => r.data),
  });

  const { data: rules } = useQuery<RuleRow[]>({
    queryKey: ["categoryRules"],
    queryFn: () => api.get("/api/category-rules").then((r) => r.data),
  });

  const rulesByCategory = useMemo(() => {
    const map: Record<string, RuleRow[]> = {};
    for (const r of rules ?? []) (map[r.categoryId] ??= []).push(r);
    return map;
  }, [rules]);

  function toggleExpanded(id: string) {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  const form = useForm<CreateCategoryInput>({
    resolver: zodResolver(createCategorySchema),
    defaultValues: { name: "", color: DEFAULT_COLOR, bucket: undefined },
  });
  const color = form.watch("color");

  const saveMutation = useMutation({
    mutationFn: (body: CreateCategoryInput) =>
      editing
        ? api.patch(`/api/categories/${editing.id}`, body).then((r) => r.data)
        : api.post("/api/categories", body).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["categories"] });
      // Renaming/recolouring a category changes how every transaction referencing it
      // should render, plus every aggregate that groups by category name.
      queryClient.invalidateQueries({ queryKey: ["transactions"] });
      queryClient.invalidateQueries({ queryKey: ["summary"] });
      queryClient.invalidateQueries({ queryKey: ["analytics"] });
      setEditorOpen(false);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => api.delete(`/api/categories/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["categories"] });
      queryClient.invalidateQueries({ queryKey: ["categoryRules"] });
      setDeleteTarget(null);
    },
    onError: (err) => setDeleteError(apiError(err)),
  });

  function openEditor(cat?: CategoryRow) {
    setEditing(cat ?? null);
    saveMutation.reset();
    form.reset(
      cat
        ? { name: cat.name, color: cat.color, bucket: cat.bucket ?? undefined }
        : { name: "", color: DEFAULT_COLOR, bucket: undefined },
    );
    setEditorOpen(true);
  }

  return (
    <div className="max-w-3xl mx-auto space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold text-foreground">Categories</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Add, rename, recolour, and remove transaction categories. Expand a category to manage
            its auto-categorize rules.
          </p>
        </div>
        <Button size="sm" onClick={() => openEditor()}>
          <Plus className="h-4 w-4 mr-1" />
          Add Category
        </Button>
      </div>

      <Card>
        <CardContent className="pt-6">
          {isPending ? (
            <Skeleton className="h-40 w-full" />
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Category</TableHead>
                  <TableHead>Bucket</TableHead>
                  <TableHead className="text-right">Transactions</TableHead>
                  <TableHead />
                </TableRow>
              </TableHeader>
              <TableBody>
                {(categories ?? []).map((c) => {
                  const categoryRules = rulesByCategory[c.id] ?? [];
                  const isExpanded = expanded.has(c.id);
                  return (
                    <Fragment key={c.id}>
                      <TableRow>
                        <TableCell className="font-medium">
                          <button
                            type="button"
                            onClick={() => toggleExpanded(c.id)}
                            className="flex items-center gap-2.5 text-left"
                          >
                            <ChevronRight
                              className={`h-3.5 w-3.5 text-muted-foreground shrink-0 transition-transform ${
                                isExpanded ? "rotate-90" : ""
                              }`}
                            />
                            <span
                              className="inline-block h-3.5 w-3.5 rounded-full ring-1 ring-black/10 dark:ring-white/15 shrink-0"
                              style={{ backgroundColor: c.color }}
                            />
                            {c.name}
                            {categoryRules.length > 0 && (
                              <span className="text-[11px] font-normal text-muted-foreground">
                                ({categoryRules.length} rule{categoryRules.length === 1 ? "" : "s"})
                              </span>
                            )}
                          </button>
                        </TableCell>
                        <TableCell>
                          {c.bucket ? (
                            <Badge variant="secondary">{c.bucket}</Badge>
                          ) : (
                            <span className="text-xs text-muted-foreground">—</span>
                          )}
                        </TableCell>
                        <TableCell className="text-right font-mono text-muted-foreground">
                          {c.transactionCount.toLocaleString()}
                        </TableCell>
                        <TableCell>
                          <div className="flex items-center gap-1 justify-end">
                            <Button
                              variant="ghost"
                              size="icon"
                              className="h-7 w-7"
                              title="Edit"
                              onClick={() => openEditor(c)}
                            >
                              <Pencil className="h-4 w-4" />
                            </Button>
                            <Button
                              variant="ghost"
                              size="icon"
                              className="h-7 w-7 text-destructive hover:text-destructive"
                              title="Delete"
                              onClick={() => {
                                setDeleteError(null);
                                setDeleteTarget(c);
                              }}
                            >
                              <Trash2 className="h-4 w-4" />
                            </Button>
                          </div>
                        </TableCell>
                      </TableRow>
                      {isExpanded && (
                        <TableRow className="hover:bg-transparent">
                          <TableCell colSpan={4} className="p-0">
                            <CategoryRulesPanel category={c} rules={categoryRules} />
                          </TableCell>
                        </TableRow>
                      )}
                    </Fragment>
                  );
                })}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      {/* Add / Edit dialog */}
      <Dialog open={editorOpen} onOpenChange={setEditorOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{editing ? "Edit Category" : "Add Category"}</DialogTitle>
          </DialogHeader>
          <form onSubmit={form.handleSubmit((d) => saveMutation.mutate(d))} noValidate>
            <div className="space-y-4 py-2">
              <div className="space-y-1">
                <Label htmlFor="name">Name</Label>
                <Input
                  id="name"
                  placeholder="e.g. Fitness"
                  {...form.register("name")}
                  className={form.formState.errors.name ? "border-destructive" : ""}
                />
                {form.formState.errors.name && (
                  <p className="text-xs text-destructive">{form.formState.errors.name.message}</p>
                )}
              </div>

              <div className="space-y-1">
                <Label htmlFor="color">Colour</Label>
                <div className="flex items-center gap-3">
                  <input
                    id="color"
                    type="color"
                    value={color}
                    onChange={(e) => form.setValue("color", e.target.value)}
                    className="h-9 w-12 cursor-pointer rounded-md border border-input bg-background p-1"
                  />
                  <Input
                    aria-label="Hex colour"
                    {...form.register("color")}
                    className={`font-mono w-32 ${form.formState.errors.color ? "border-destructive" : ""}`}
                  />
                </div>
                {form.formState.errors.color && (
                  <p className="text-xs text-destructive">{form.formState.errors.color.message}</p>
                )}
              </div>

              <div className="space-y-1">
                <Label htmlFor="bucket">Bucket</Label>
                <select
                  id="bucket"
                  {...form.register("bucket", {
                    setValueAs: (v) => (v === "" ? undefined : v),
                  })}
                  className="flex h-9 w-full rounded-md border border-input bg-background px-3 py-1 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                >
                  {/* Unset is birth-only — offered while the category has no Bucket, gone once chosen. */}
                  {!editing?.bucket && <option value="">Unset</option>}
                  {buckets.map((b) => (
                    <option key={b} value={b}>
                      {b}
                    </option>
                  ))}
                </select>
                <p className="text-xs text-muted-foreground">
                  Needs = bills/rent · Wants = discretionary spend · Savings = money put aside ·
                  Ignore = skip. Stamped onto this category's expenses on import; leave Unset if
                  unclear.
                </p>
              </div>

              {saveMutation.isError && (
                <p className="text-sm text-destructive">{apiError(saveMutation.error)}</p>
              )}
            </div>

            <DialogFooter className="mt-4">
              <Button type="button" variant="outline" onClick={() => setEditorOpen(false)}>
                Cancel
              </Button>
              <Button type="submit" disabled={saveMutation.isPending}>
                {saveMutation.isPending ? "Saving..." : editing ? "Save" : "Add Category"}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>

      {/* Delete confirmation */}
      <AlertDialog
        open={!!deleteTarget}
        onOpenChange={(open) => {
          if (!open) {
            setDeleteTarget(null);
            setDeleteError(null);
          }
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete "{deleteTarget?.name}"?</AlertDialogTitle>
            <AlertDialogDescription>
              This permanently removes the category and any auto-categorize rules that target it. It
              can't be deleted while transactions still use it.
            </AlertDialogDescription>
          </AlertDialogHeader>
          {deleteError && <p className="text-sm text-destructive">{deleteError}</p>}
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              className="bg-red-500 hover:bg-red-600 text-white"
              onClick={(e) => {
                e.preventDefault();
                if (deleteTarget) deleteMutation.mutate(deleteTarget.id);
              }}
              disabled={deleteMutation.isPending}
            >
              {deleteMutation.isPending ? "Deleting..." : "Delete"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}

// ─── Auto-categorize rules panel ───────────────────────────────────────────────
// Nested under its category row (expand/collapse) rather than a separate
// side-by-side pane, so the same layout works unchanged on mobile.

function CategoryRulesPanel({ category, rules }: { category: CategoryRow; rules: RuleRow[] }) {
  const queryClient = useQueryClient();
  const [editingId, setEditingId] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  // A rule that was just created/edited — offer to run it over existing
  // transactions (Outlook-style "run rule now"), never applied silently.
  const [pendingApplyRule, setPendingApplyRule] = useState<RuleRow | null>(null);

  const form = useForm<CreateCategoryRuleInput>({
    resolver: zodResolver(createCategoryRuleSchema),
    defaultValues: { pattern: "", bank: null, categoryId: category.id },
  });

  const saveMutation = useMutation({
    mutationFn: (body: CreateCategoryRuleInput) =>
      editingId
        ? api.patch(`/api/category-rules/${editingId}`, body).then((r) => r.data as RuleRow)
        : api.post("/api/category-rules", body).then((r) => r.data as RuleRow),
    onSuccess: (rule) => {
      queryClient.invalidateQueries({ queryKey: ["categoryRules"] });
      setMessage(null);
      setEditingId(null);
      form.reset({ pattern: "", bank: null, categoryId: category.id });
      setPendingApplyRule(rule);
    },
  });

  const applyMutation = useMutation({
    mutationFn: (id: string) =>
      api.post(`/api/category-rules/${id}/apply`).then((r) => r.data as { recategorized: number }),
    onSuccess: ({ recategorized }) => {
      queryClient.invalidateQueries({ queryKey: ["categories"] });
      queryClient.invalidateQueries({ queryKey: ["transactions"] });
      // Recategorising shifts every per-category aggregate.
      queryClient.invalidateQueries({ queryKey: ["summary"] });
      queryClient.invalidateQueries({ queryKey: ["analytics"] });
      setMessage(
        `Recategorized ${recategorized} existing transaction${recategorized === 1 ? "" : "s"}.`,
      );
      setPendingApplyRule(null);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => api.delete(`/api/category-rules/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["categoryRules"] }),
  });

  function startEdit(rule: RuleRow) {
    setEditingId(rule.id);
    setMessage(null);
    form.reset({ pattern: rule.pattern, bank: rule.bank, categoryId: category.id });
  }

  function cancelEdit() {
    setEditingId(null);
    form.reset({ pattern: "", bank: null, categoryId: category.id });
  }

  return (
    <div className="space-y-3 py-3 px-4 sm:px-10 bg-muted/30">
      {rules.length === 0 ? (
        <p className="text-xs text-muted-foreground">
          No auto-categorize rules yet — add one below to route matching descriptions here
          automatically.
        </p>
      ) : (
        <ul className="space-y-1.5">
          {rules.map((r) => (
            <li key={r.id} className="flex items-center justify-between gap-2">
              <div className="flex items-center gap-2 min-w-0">
                <span className="font-mono text-xs bg-background border border-input rounded px-1.5 py-0.5 truncate">
                  {r.pattern}
                </span>
                {r.bank ? (
                  <span
                    className={`shrink-0 px-1.5 py-0.5 rounded-full text-[10px] font-medium border ${SOURCE_STYLES[BANK_LABELS[r.bank]]}`}
                  >
                    {BANK_LABELS[r.bank]}
                  </span>
                ) : (
                  <span className="shrink-0 px-1.5 py-0.5 rounded-full text-[10px] font-medium border text-muted-foreground border-muted-foreground/40">
                    Any bank
                  </span>
                )}
              </div>
              <div className="flex items-center gap-1 shrink-0">
                <Button
                  variant="ghost"
                  size="icon"
                  className="h-6 w-6"
                  title="Edit rule"
                  onClick={() => startEdit(r)}
                >
                  <Pencil className="h-3 w-3" />
                </Button>
                <Button
                  variant="ghost"
                  size="icon"
                  className="h-6 w-6 text-destructive hover:text-destructive"
                  title="Delete rule"
                  onClick={() => deleteMutation.mutate(r.id)}
                >
                  <Trash2 className="h-3 w-3" />
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}

      <form
        onSubmit={form.handleSubmit((d) => saveMutation.mutate({ ...d, categoryId: category.id }))}
        className="flex flex-col sm:flex-row items-stretch sm:items-center gap-2"
      >
        <Input
          placeholder='Pattern, e.g. "Payment Thank You-Mobile" (or AMAZON* for wildcard)'
          {...form.register("pattern")}
          className="h-8 text-xs flex-1"
        />
        <select
          value={form.watch("bank") ?? ""}
          onChange={(e) =>
            form.setValue("bank", e.target.value === "" ? null : (e.target.value as KnownBank))
          }
          className="h-8 rounded-md border border-input bg-background px-2 text-xs shrink-0"
        >
          <option value="">Any bank</option>
          {KNOWN_BANKS.map((b) => (
            <option key={b} value={b}>
              {BANK_LABELS[b]}
            </option>
          ))}
        </select>
        <div className="flex items-center gap-2 shrink-0">
          <Button type="submit" size="sm" className="h-8" disabled={saveMutation.isPending}>
            {editingId ? "Save" : "Add rule"}
          </Button>
          {editingId && (
            <Button type="button" variant="outline" size="sm" className="h-8" onClick={cancelEdit}>
              Cancel
            </Button>
          )}
        </div>
      </form>
      {form.formState.errors.pattern && (
        <p className="text-xs text-destructive">{form.formState.errors.pattern.message}</p>
      )}
      {saveMutation.isError && (
        <p className="text-xs text-destructive">{apiError(saveMutation.error)}</p>
      )}
      {message && <p className="text-xs text-emerald-600 dark:text-emerald-400">{message}</p>}

      <AlertDialog
        open={!!pendingApplyRule}
        onOpenChange={(open) => {
          if (!open) setPendingApplyRule(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Run this rule over existing transactions?</AlertDialogTitle>
            <AlertDialogDescription>
              "{pendingApplyRule?.pattern}"
              {pendingApplyRule?.bank
                ? ` on ${BANK_LABELS[pendingApplyRule.bank]}`
                : " on any bank"}{" "}
              will recategorize any matching transaction into "{category.name}". The rule applies to
              future imports either way — this only affects transactions you already have.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel onClick={() => setPendingApplyRule(null)}>Not now</AlertDialogCancel>
            <AlertDialogAction
              onClick={(e) => {
                e.preventDefault();
                if (pendingApplyRule) applyMutation.mutate(pendingApplyRule.id);
              }}
              disabled={applyMutation.isPending}
            >
              {applyMutation.isPending ? "Applying..." : "Yes, apply now"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
