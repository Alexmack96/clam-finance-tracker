import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { RefreshCw, CheckCircle2, AlertTriangle } from "lucide-react";
import api from "../lib/api.js";
import { Card, CardContent, CardHeader, CardTitle } from "../components/ui/card.js";
import { Button } from "../components/ui/button.js";
import { Badge } from "../components/ui/badge.js";

type RecMissingTx = { id: string; created: string; amountPence: number; description: string };

type RecAccountResult = {
  accountId: string;
  accountType: string;
  apiSettledCount: number;
  missingCount: number;
  missing: RecMissingTx[];
  error?: string;
};

type RecRun = {
  id: string;
  ranAt: string;
  window: string;
  trigger: "sync" | "manual";
  totalMissing: number;
  results: RecAccountResult[];
};

const fmtGbp = (pence: number) => `${pence < 0 ? "−" : ""}£${(Math.abs(pence) / 100).toFixed(2)}`;

function accountLabel(type: string): string {
  if (type === "uk_retail") return "Debit";
  if (type === "uk_monzo_flex") return "Flex";
  return type;
}

export function AdminPage() {
  const queryClient = useQueryClient();

  const { data: runs, isLoading } = useQuery<RecRun[]>({
    queryKey: ["monzo-rec"],
    queryFn: () => api.get("/api/admin/monzo/rec").then((r) => r.data),
  });

  const runNow = useMutation<RecRun, Error>({
    mutationFn: () => api.post("/api/admin/monzo/rec/run").then((r) => r.data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["monzo-rec"] }),
  });

  return (
    <div className="max-w-5xl mx-auto space-y-6">
      <div>
        <h1 className="text-3xl font-bold text-foreground">Admin</h1>
        <p className="text-sm text-muted-foreground uppercase tracking-wide mt-1">
          Diagnostics &amp; data health
        </p>
      </div>

      <Card>
        <CardHeader className="pb-2 flex flex-row items-start justify-between gap-4">
          <div>
            <CardTitle className="text-xs uppercase tracking-wide text-muted-foreground">
              Monzo reconciliation
            </CardTitle>
            <p className="text-sm text-muted-foreground mt-1 max-w-prose">
              Compares the last 90 days of the Monzo API against staged transactions. Runs
              automatically after every sync; any gaps are also reported to Sentry.
            </p>
          </div>
          <Button
            size="sm"
            disabled={runNow.isPending}
            onClick={() => runNow.mutate()}
            className="shrink-0"
          >
            <RefreshCw className={`size-4 ${runNow.isPending ? "animate-spin" : ""}`} />
            {runNow.isPending ? "Running…" : "Run now"}
          </Button>
        </CardHeader>
        <CardContent className="space-y-4">
          {runNow.isError && <p className="text-sm text-destructive">{runNow.error.message}</p>}

          {isLoading ? (
            <p className="text-sm text-muted-foreground">Loading…</p>
          ) : !runs || runs.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              No reconciliation runs yet. Sync Monzo or click Run now.
            </p>
          ) : (
            <div className="space-y-3">
              {runs.map((run) => (
                <RunRow key={run.id} run={run} />
              ))}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function RunRow({ run }: { run: RecRun }) {
  const clean = run.totalMissing === 0;
  const hasMissing = run.results.some((a) => a.missing.length > 0);

  return (
    <div className="rounded-lg border border-border bg-card/50 p-4 space-y-3">
      <div className="flex items-center justify-between gap-3 flex-wrap">
        <div className="flex items-center gap-2">
          {clean ? (
            <Badge variant="secondary" className="gap-1">
              <CheckCircle2 className="size-3" /> Reconciled
            </Badge>
          ) : (
            <Badge variant="destructive" className="gap-1">
              <AlertTriangle className="size-3" /> {run.totalMissing} missing
            </Badge>
          )}
          <Badge variant="outline">{run.trigger}</Badge>
          <span className="text-xs text-muted-foreground">{run.window} window</span>
        </div>
        <span className="text-xs text-muted-foreground">
          {new Date(run.ranAt).toLocaleString()}
        </span>
      </div>

      <div className="space-y-1">
        {run.results.map((acc) => (
          <div key={acc.accountId} className="text-sm flex items-center gap-2">
            <span className="font-medium text-foreground">{accountLabel(acc.accountType)}</span>
            {acc.error ? (
              <span className="text-destructive">fetch failed — {acc.error}</span>
            ) : (
              <span className="text-muted-foreground">
                {acc.apiSettledCount} checked · {acc.missingCount} missing
              </span>
            )}
          </div>
        ))}
      </div>

      {hasMissing && (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs uppercase tracking-wide text-muted-foreground">
                <th className="py-1 pr-4 font-medium">Date</th>
                <th className="py-1 pr-4 font-medium">Description</th>
                <th className="py-1 pr-4 font-medium">Amount</th>
                <th className="py-1 font-medium">Account</th>
              </tr>
            </thead>
            <tbody>
              {run.results.flatMap((acc) =>
                acc.missing.map((tx) => (
                  <tr key={tx.id} className="border-t border-border/60">
                    <td className="py-1 pr-4 whitespace-nowrap">
                      {new Date(tx.created).toLocaleDateString()}
                    </td>
                    <td className="py-1 pr-4">{tx.description}</td>
                    <td className="py-1 pr-4 whitespace-nowrap font-mono">
                      {fmtGbp(tx.amountPence)}
                    </td>
                    <td className="py-1 whitespace-nowrap text-muted-foreground">
                      {accountLabel(acc.accountType)}
                    </td>
                  </tr>
                )),
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
