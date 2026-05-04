import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { CheckCircle, Trash2, Plus } from "lucide-react";
import { createTabSchema, type CreateTabInput, type Tab } from "@clam/core";
import api from "../lib/api.js";
import { Card, CardContent, CardHeader, CardTitle } from "../components/ui/card.js";
import { Button } from "../components/ui/button.js";
import { Input } from "../components/ui/input.js";
import { Label } from "../components/ui/label.js";
import { Badge } from "../components/ui/badge.js";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "../components/ui/dialog.js";
import { AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from "../components/ui/alert-dialog.js";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "../components/ui/select.js";
import { Skeleton } from "../components/ui/skeleton.js";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "../components/ui/table.js";

const fmt = (n: string | number) =>
  new Intl.NumberFormat("en-GB", { style: "currency", currency: "GBP" }).format(
    typeof n === "string" ? parseFloat(n) : n
  );

const fmtDate = (d: string | null) =>
  d ? new Date(d).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" }) : "—";

function TabTable({
  tabs,
  onSettle,
  onDelete,
}: {
  tabs: Tab[];
  onSettle: (tab: Tab) => void;
  onDelete: (tab: Tab) => void;
}) {
  if (tabs.length === 0) {
    return <p className="text-sm text-muted-foreground py-4">Nothing here.</p>;
  }

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Person</TableHead>
          <TableHead>Description</TableHead>
          <TableHead className="text-right">Amount</TableHead>
          <TableHead>Due</TableHead>
          <TableHead>Note</TableHead>
          <TableHead>Status</TableHead>
          <TableHead />
        </TableRow>
      </TableHeader>
      <TableBody>
        {tabs.map((tab) => (
          <TableRow key={tab.id} className={tab.status === "Settled" ? "opacity-50" : undefined}>
            <TableCell className="font-medium">{tab.person}</TableCell>
            <TableCell>{tab.description}</TableCell>
            <TableCell className="text-right font-mono">{fmt(tab.amount)}</TableCell>
            <TableCell className="text-sm text-muted-foreground">{fmtDate(tab.dueDate)}</TableCell>
            <TableCell className="text-sm text-muted-foreground">{tab.note ?? "—"}</TableCell>
            <TableCell>
              <Badge variant={tab.status === "Settled" ? "secondary" : "default"}>
                {tab.status}
              </Badge>
            </TableCell>
            <TableCell>
              {tab.status === "Open" && (
                <div className="flex items-center gap-1 justify-end">
                  <Button
                    variant="ghost"
                    size="icon"
                    className="h-7 w-7 text-emerald-600 hover:text-emerald-700"
                    title="Mark settled"
                    onClick={() => onSettle(tab)}
                  >
                    <CheckCircle className="h-4 w-4" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    className="h-7 w-7 text-destructive hover:text-destructive"
                    title="Delete"
                    onClick={() => onDelete(tab)}
                  >
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </div>
              )}
              {tab.status === "Settled" && (
                <Button
                  variant="ghost"
                  size="icon"
                  className="h-7 w-7 text-destructive hover:text-destructive"
                  title="Delete"
                  onClick={() => onDelete(tab)}
                >
                  <Trash2 className="h-4 w-4" />
                </Button>
              )}
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

export function TabsPage() {
  const queryClient = useQueryClient();
  const [showAll, setShowAll] = useState(false);
  const [addOpen, setAddOpen] = useState(false);
  const [settleTarget, setSettleTarget] = useState<Tab | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<Tab | null>(null);

  const { data, isPending } = useQuery({
    queryKey: ["tabs", showAll ? "all" : "open"],
    queryFn: () =>
      api.get(`/api/tabs${showAll ? "?status=all" : ""}`).then((r) => r.data),
  });

  const tabs: Tab[] = data?.tabs ?? [];
  const totals = data?.totals ?? { theyOweMe: "0.00", iOweThem: "0.00" };

  const theyOweMeTabs = tabs.filter((t) => t.direction === "TheyOwe");
  const iOweThem = tabs.filter((t) => t.direction === "IOwe");

  const form = useForm<CreateTabInput>({
    resolver: zodResolver(createTabSchema),
    defaultValues: { person: "", description: "", amount: 0, direction: "TheyOwe", dueDate: null, note: null },
  });

  const createMutation = useMutation({
    mutationFn: (body: CreateTabInput) => api.post("/api/tabs", body).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["tabs"] });
      setAddOpen(false);
      form.reset();
    },
  });

  const settleMutation = useMutation({
    mutationFn: (id: string) => api.patch(`/api/tabs/${id}`, { status: "Settled" }).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["tabs"] });
      setSettleTarget(null);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => api.delete(`/api/tabs/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["tabs"] });
      setDeleteTarget(null);
    },
  });

  function onSubmit(data: CreateTabInput) {
    createMutation.mutate(data);
  }

  return (
    <div className="max-w-5xl mx-auto space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold text-foreground">Tabs</h1>
          <p className="text-sm text-muted-foreground mt-1">Track informal debts — who owes who</p>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" size="sm" onClick={() => setShowAll((v) => !v)}>
            {showAll ? "Hide Settled" : "Show Settled"}
          </Button>
          <Button size="sm" onClick={() => setAddOpen(true)}>
            <Plus className="h-4 w-4 mr-1" />
            Add Tab
          </Button>
        </div>
      </div>

      {/* Summary cards */}
      <div className="grid grid-cols-2 gap-4">
        <Card className="border-emerald-500/40">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs uppercase tracking-wide text-muted-foreground">Owed to Me</CardTitle>
          </CardHeader>
          <CardContent>
            {isPending ? (
              <Skeleton className="h-8 w-32" />
            ) : (
              <span className="text-3xl font-bold text-emerald-500">{fmt(totals.theyOweMe)}</span>
            )}
          </CardContent>
        </Card>
        <Card className="border-amber-500/40">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs uppercase tracking-wide text-muted-foreground">I Owe Them</CardTitle>
          </CardHeader>
          <CardContent>
            {isPending ? (
              <Skeleton className="h-8 w-32" />
            ) : (
              <span className="text-3xl font-bold text-amber-500">{fmt(totals.iOweThem)}</span>
            )}
          </CardContent>
        </Card>
      </div>

      {/* Owed to Me */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold">Owed to Me</CardTitle>
        </CardHeader>
        <CardContent>
          {isPending ? (
            <Skeleton className="h-24 w-full" />
          ) : (
            <TabTable
              tabs={theyOweMeTabs}
              onSettle={setSettleTarget}
              onDelete={setDeleteTarget}
            />
          )}
        </CardContent>
      </Card>

      {/* I Owe Them */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold">I Owe Them</CardTitle>
        </CardHeader>
        <CardContent>
          {isPending ? (
            <Skeleton className="h-24 w-full" />
          ) : (
            <TabTable
              tabs={iOweThem}
              onSettle={setSettleTarget}
              onDelete={setDeleteTarget}
            />
          )}
        </CardContent>
      </Card>

      {/* Add Tab Dialog */}
      <Dialog open={addOpen} onOpenChange={setAddOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Add Tab</DialogTitle>
          </DialogHeader>
          <form onSubmit={form.handleSubmit(onSubmit)} noValidate>
            <div className="space-y-4 py-2">
              <div className="grid grid-cols-2 gap-4">
                <div className="space-y-1">
                  <Label htmlFor="person">Person</Label>
                  <Input
                    id="person"
                    placeholder="e.g. James"
                    {...form.register("person")}
                    className={form.formState.errors.person ? "border-destructive" : ""}
                  />
                  {form.formState.errors.person && (
                    <p className="text-xs text-destructive">{form.formState.errors.person.message}</p>
                  )}
                </div>
                <div className="space-y-1">
                  <Label htmlFor="direction">Direction</Label>
                  <Select
                    defaultValue="TheyOwe"
                    onValueChange={(v) => form.setValue("direction", v as "IOwe" | "TheyOwe")}
                  >
                    <SelectTrigger id="direction">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="TheyOwe">Owed to me</SelectItem>
                      <SelectItem value="IOwe">I owe them</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
              </div>

              <div className="space-y-1">
                <Label htmlFor="description">Description</Label>
                <Input
                  id="description"
                  placeholder="e.g. Finsbury Park tennis court"
                  {...form.register("description")}
                  className={form.formState.errors.description ? "border-destructive" : ""}
                />
                {form.formState.errors.description && (
                  <p className="text-xs text-destructive">{form.formState.errors.description.message}</p>
                )}
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div className="space-y-1">
                  <Label htmlFor="amount">Amount (£)</Label>
                  <Input
                    id="amount"
                    type="number"
                    step="0.01"
                    min="0"
                    placeholder="0.00"
                    {...form.register("amount", { valueAsNumber: true })}
                    className={form.formState.errors.amount ? "border-destructive" : ""}
                  />
                  {form.formState.errors.amount && (
                    <p className="text-xs text-destructive">{form.formState.errors.amount.message}</p>
                  )}
                </div>
                <div className="space-y-1">
                  <Label htmlFor="dueDate">Due date (optional)</Label>
                  <Input
                    id="dueDate"
                    type="date"
                    {...form.register("dueDate")}
                  />
                </div>
              </div>

              <div className="space-y-1">
                <Label htmlFor="note">Note (optional)</Label>
                <Input
                  id="note"
                  placeholder="Any extra context"
                  {...form.register("note")}
                />
              </div>
            </div>

            <DialogFooter className="mt-4">
              <Button type="button" variant="outline" onClick={() => { setAddOpen(false); form.reset(); }}>
                Cancel
              </Button>
              <Button type="submit" disabled={createMutation.isPending}>
                {createMutation.isPending ? "Adding..." : "Add Tab"}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>

      {/* Settle confirmation */}
      <Dialog open={!!settleTarget} onOpenChange={(open) => !open && setSettleTarget(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Mark as settled?</DialogTitle>
          </DialogHeader>
          <p className="text-sm text-muted-foreground">
            {settleTarget?.direction === "TheyOwe"
              ? `Mark ${fmt(settleTarget?.amount ?? 0)} owed to you by ${settleTarget?.person} as settled?`
              : `Mark ${fmt(settleTarget?.amount ?? 0)} owed to ${settleTarget?.person} as settled?`}
          </p>
          <DialogFooter className="mt-4">
            <Button variant="outline" onClick={() => setSettleTarget(null)}>
              Cancel
            </Button>
            <Button
              onClick={() => settleTarget && settleMutation.mutate(settleTarget.id)}
              disabled={settleMutation.isPending}
              className="bg-emerald-600 hover:bg-emerald-700 text-white"
            >
              {settleMutation.isPending ? "Settling..." : "Mark Settled"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Delete confirmation */}
      <AlertDialog open={!!deleteTarget} onOpenChange={(open) => !open && setDeleteTarget(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete tab?</AlertDialogTitle>
            <AlertDialogDescription>
              This will permanently delete the tab with {deleteTarget?.person} for {fmt(deleteTarget?.amount ?? 0)}. This cannot be undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              className="bg-red-500 hover:bg-red-600 text-white"
              onClick={() => deleteTarget && deleteMutation.mutate(deleteTarget.id)}
            >
              Delete
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
