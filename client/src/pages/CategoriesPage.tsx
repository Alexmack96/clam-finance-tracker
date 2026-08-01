import { Fragment, useMemo, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { AxiosError } from "axios";
import { Link } from "react-router-dom";
import { Pencil, Trash2, Plus, ChevronRight, ArrowUpRight } from "lucide-react";
import {
  createCategorySchema,
  RULE_FIELD_LABELS,
  RULE_OPERATOR_LABELS,
  type CreateCategoryInput,
  type Category,
  type KnownBank,
  type Rule,
} from "@clam/core";
import api from "../lib/api.js";
import { SOURCE_STYLES, type BankSource } from "../lib/bankSource.js";
import { Card, CardContent } from "../components/ui/card.js";
import { Button } from "../components/ui/button.js";
import { Input } from "../components/ui/input.js";
import { Label } from "../components/ui/label.js";
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

  const { data: rules } = useQuery<Rule[]>({
    queryKey: ["rules"],
    queryFn: () => api.get("/api/rules").then((r) => r.data),
  });

  const rulesByCategory = useMemo(() => {
    const map: Record<string, Rule[]> = {};
    for (const r of rules ?? []) {
      if (r.kind === "Category" && r.categoryId) (map[r.categoryId] ??= []).push(r);
    }
    for (const list of Object.values(map)) list.sort((a, b) => a.position - b.position);
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
    defaultValues: { name: "", color: DEFAULT_COLOR },
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
      // A rename rewrites the exact-match conditions of any Bucket rule on it.
      queryClient.invalidateQueries({ queryKey: ["rules"] });
      setEditorOpen(false);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => api.delete(`/api/categories/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["categories"] });
      queryClient.invalidateQueries({ queryKey: ["rules"] });
      setDeleteTarget(null);
    },
    onError: (err) => setDeleteError(apiError(err)),
  });

  function openEditor(cat?: CategoryRow) {
    setEditing(cat ?? null);
    saveMutation.reset();
    form.reset(cat ? { name: cat.name, color: cat.color } : { name: "", color: DEFAULT_COLOR });
    setEditorOpen(true);
  }

  return (
    <div className="max-w-3xl mx-auto space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold text-foreground">Categories</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Add, rename, recolour, and remove transaction categories. Expand one to see which rules
            route into it — rules are edited and ordered on the{" "}
            <Link to="/rules" className="underline underline-offset-2">
              Rules
            </Link>{" "}
            page.
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
                          <TableCell colSpan={3} className="p-0">
                            <CategoryRulesSummary rules={categoryRules} />
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

              <p className="text-xs text-muted-foreground">
                A category no longer carries a default Bucket — bucketing is decided by Bucket rules
                on the Rules page, so there is one place to look when asking why a transaction
                landed where it did. Renaming a category updates any rule that matches it by exact
                name.
              </p>

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
              This permanently removes the category and any rules that target it. It can't be
              deleted while transactions still use it.
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

// ─── Rules that route here (read-only) ───────────────────────────────────────
// Deliberately not editable. Precedence is global and ordered, so a per-category
// editor would show rules in an order that isn't the one that runs — and two
// editors for the same object would drift.

function CategoryRulesSummary({ rules }: { rules: Rule[] }) {
  return (
    <div className="space-y-2 py-3 px-4 sm:px-10 bg-muted/30">
      {rules.length === 0 ? (
        <p className="text-xs text-muted-foreground">
          No rules route into this category.{" "}
          <Link to="/rules" className="underline underline-offset-2">
            Add one on the Rules page
          </Link>
          .
        </p>
      ) : (
        <>
          <ul className="space-y-1.5">
            {rules.map((r) => (
              <li key={r.id} className="flex items-center gap-2 min-w-0">
                <span className="w-6 shrink-0 text-center text-[10px] font-mono text-muted-foreground">
                  {r.position + 1}
                </span>
                <span className="flex flex-wrap items-center gap-1 min-w-0">
                  {r.conditions.map((c, i) => (
                    <span key={c.id} className="flex items-center gap-1">
                      {i > 0 && (
                        <span className="text-[10px] font-semibold text-muted-foreground">
                          {c.negate ? "EXCEPT" : r.joinOperator}
                        </span>
                      )}
                      <span
                        className={`font-mono text-[11px] rounded px-1.5 py-0.5 truncate max-w-[16rem] ${
                          c.negate ? "bg-destructive/10 text-destructive" : "bg-background border"
                        }`}
                      >
                        {RULE_FIELD_LABELS[c.field]} {RULE_OPERATOR_LABELS[c.operator]} “{c.value}”
                      </span>
                    </span>
                  ))}
                </span>
                {r.bank && (
                  <span
                    className={`shrink-0 px-1.5 py-0.5 rounded-full text-[10px] font-medium border ${SOURCE_STYLES[BANK_LABELS[r.bank]]}`}
                  >
                    {BANK_LABELS[r.bank]}
                  </span>
                )}
              </li>
            ))}
          </ul>
          <Link
            to="/rules"
            className="inline-flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground underline underline-offset-2"
          >
            Edit and reorder on the Rules page
            <ArrowUpRight className="h-3 w-3" />
          </Link>
        </>
      )}
    </div>
  );
}
