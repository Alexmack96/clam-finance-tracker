import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { AxiosError } from "axios";
import { Pencil, Trash2, Plus } from "lucide-react";
import {
  createCategorySchema,
  savingTypes,
  type CreateCategoryInput,
  type Category,
} from "@clam/core";
import api from "../lib/api.js";
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

const DEFAULT_COLOR = "#14b8a6";

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

  const { data: categories, isPending } = useQuery<CategoryRow[]>({
    queryKey: ["categories"],
    queryFn: () => api.get("/api/categories").then((r) => r.data),
  });

  const form = useForm<CreateCategoryInput>({
    resolver: zodResolver(createCategorySchema),
    defaultValues: { name: "", color: DEFAULT_COLOR, savingType: "Fun" },
  });
  const color = form.watch("color");

  const saveMutation = useMutation({
    mutationFn: (body: CreateCategoryInput) =>
      editing
        ? api.patch(`/api/categories/${editing.id}`, body).then((r) => r.data)
        : api.post("/api/categories", body).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["categories"] });
      setEditorOpen(false);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => api.delete(`/api/categories/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["categories"] });
      setDeleteTarget(null);
    },
    onError: (err) => setDeleteError(apiError(err)),
  });

  function openEditor(cat?: CategoryRow) {
    setEditing(cat ?? null);
    saveMutation.reset();
    form.reset(
      cat
        ? { name: cat.name, color: cat.color, savingType: cat.savingType }
        : { name: "", color: DEFAULT_COLOR, savingType: "Fun" },
    );
    setEditorOpen(true);
  }

  return (
    <div className="max-w-3xl mx-auto space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold text-foreground">Categories</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Add, rename, recolour, and remove transaction categories
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
                  <TableHead>Type</TableHead>
                  <TableHead className="text-right">Transactions</TableHead>
                  <TableHead />
                </TableRow>
              </TableHeader>
              <TableBody>
                {(categories ?? []).map((c) => (
                  <TableRow key={c.id}>
                    <TableCell className="font-medium">
                      <div className="flex items-center gap-2.5">
                        <span
                          className="inline-block h-3.5 w-3.5 rounded-full ring-1 ring-black/10 dark:ring-white/15"
                          style={{ backgroundColor: c.color }}
                        />
                        {c.name}
                      </div>
                    </TableCell>
                    <TableCell>
                      <Badge variant="secondary">{c.savingType}</Badge>
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
                ))}
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
                <Label htmlFor="savingType">Saving type</Label>
                <select
                  id="savingType"
                  {...form.register("savingType")}
                  className="flex h-9 w-full rounded-md border border-input bg-background px-3 py-1 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                >
                  {savingTypes.map((t) => (
                    <option key={t} value={t}>
                      {t}
                    </option>
                  ))}
                </select>
                <p className="text-xs text-muted-foreground">
                  Fixed = bills/rent · Fun = discretionary spend · Saving = money put aside (counts
                  toward your savings score). Transactions inherit this but can override it.
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
              This permanently removes the category. It can't be deleted while transactions still
              use it.
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
