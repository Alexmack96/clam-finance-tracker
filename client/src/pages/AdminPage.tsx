import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  RefreshCw,
  CheckCircle2,
  AlertTriangle,
  ChevronRight,
  ArrowDownToLine,
} from "lucide-react";
import api from "../lib/api.js";
import { Card, CardContent } from "../components/ui/card.js";
import { Button } from "../components/ui/button.js";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "../components/ui/collapsible.js";

type RecMissingTx = {
  id: string;
  created: string;
  amountPence: number;
  description: string;
  // Absent on runs recorded before rec became self-healing.
  recovered?: boolean;
};

type RecAccountResult = {
  accountId: string;
  accountType: string;
  apiSettledCount: number;
  missingCount: number;
  backfilledCount?: number;
  missing: RecMissingTx[];
  error?: string;
};

type RecRun = {
  id: string;
  ranAt: string;
  window: string;
  trigger: "sync" | "manual";
  totalMissing: number;
  totalBackfilled?: number;
  results: RecAccountResult[];
};

const fmtGbp = (pence: number) => `${pence < 0 ? "−" : ""}£${(Math.abs(pence) / 100).toFixed(2)}`;

function accountLabel(type: string): string {
  if (type === "uk_retail") return "Debit";
  if (type === "uk_monzo_flex") return "Flex";
  return type;
}

const DAY_FMT = new Intl.DateTimeFormat("en-GB", {
  weekday: "short",
  day: "numeric",
  month: "short",
});
const TIME_FMT = new Intl.DateTimeFormat("en-GB", {
  day: "numeric",
  month: "short",
  hour: "2-digit",
  minute: "2-digit",
});

// One gap, flattened out of its per-account bucket so the table can read as a
// single chronological ledger rather than one table per account.
type FlatTx = RecMissingTx & { accountType: string };

function flatten(results: RecAccountResult[]): FlatTx[] {
  return results
    .flatMap((acc) => acc.missing.map((tx) => ({ ...tx, accountType: acc.accountType })))
    .sort((a, b) => b.created.localeCompare(a.created));
}

// Group consecutive rows by calendar day. A day heading carries the date once, so
// the eye scans merchant names and amounts instead of re-reading the same date.
function groupByDay(txs: FlatTx[]): { key: string; label: string; rows: FlatTx[] }[] {
  const groups: { key: string; label: string; rows: FlatTx[] }[] = [];
  for (const tx of txs) {
    const key = tx.created.slice(0, 10);
    const last = groups.length > 0 ? groups[groups.length - 1] : undefined;
    if (last && last.key === key) last.rows.push(tx);
    else groups.push({ key, label: DAY_FMT.format(new Date(tx.created)), rows: [tx] });
  }
  return groups;
}

// What a run means, in one place: no gaps / gaps we closed / gaps still open.
type RunStatus = { tone: "clean" | "healed" | "broken"; label: string; blurb: string };

function runStatus(run: RecRun): RunStatus {
  const backfilled = run.totalBackfilled ?? 0;
  const unrecovered = run.totalMissing - backfilled;
  const failed = run.results.filter((a) => a.error);

  if (failed.length > 0 && run.totalMissing === 0)
    return {
      tone: "broken",
      label: "Check failed",
      blurb: `Could not read ${failed.map((a) => accountLabel(a.accountType)).join(" and ")} from the Monzo API.`,
    };
  if (run.totalMissing === 0)
    return {
      tone: "clean",
      label: "Reconciled",
      blurb: "Every settled transaction in the Monzo API is present in staging.",
    };
  if (unrecovered === 0)
    return {
      tone: "healed",
      label: `${backfilled} recovered`,
      blurb: `The incremental sync had missed ${run.totalMissing === 1 ? "this transaction" : `these ${run.totalMissing} transactions`} — all now staged and ready to process.`,
    };
  return {
    tone: "broken",
    label: `${unrecovered} still missing`,
    blurb: `Found ${run.totalMissing} gap${run.totalMissing === 1 ? "" : "s"}, recovered ${backfilled}. The rest could not be staged.`,
  };
}

const TONE_TEXT: Record<RunStatus["tone"], string> = {
  clean: "text-[var(--signal)]",
  healed: "text-[var(--signal)]",
  broken: "text-destructive",
};

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

  const [latest, ...earlier] = runs ?? [];

  return (
    <div className="max-w-4xl mx-auto space-y-10 pb-16">
      <header>
        <p className="eyebrow mb-3">Diagnostics &amp; data health</p>
        <h1 className="font-display text-[44px] leading-[1.02] font-light tracking-tight text-foreground">
          Admin
        </h1>
      </header>

      <section className="space-y-5">
        <div className="flex items-end justify-between gap-6 flex-wrap">
          <div>
            <p className="eyebrow mb-1.5">Monzo reconciliation</p>
            <p className="text-sm text-muted-foreground max-w-prose leading-relaxed">
              Re-reads the last 90 days from the Monzo API, stages anything the incremental sync
              missed, and reports the gap to Sentry. Runs automatically after every sync.
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
        </div>

        {runNow.isError && <p className="text-sm text-destructive">{runNow.error.message}</p>}

        {isLoading ? (
          <p className="text-sm text-muted-foreground">Loading…</p>
        ) : !latest ? (
          <Card>
            <CardContent className="py-10 text-center">
              <p className="text-sm text-muted-foreground">
                No reconciliation runs yet. Sync Monzo or click Run now.
              </p>
            </CardContent>
          </Card>
        ) : (
          <>
            <LatestRun run={latest} />
            {earlier.length > 0 && (
              <div className="pt-2">
                <p className="eyebrow mb-2">Earlier runs</p>
                <div className="rounded-lg border border-border overflow-hidden">
                  {earlier.map((run) => (
                    <EarlierRun key={run.id} run={run} />
                  ))}
                </div>
              </div>
            )}
          </>
        )}
      </section>
    </div>
  );
}

// The most recent run, opened up: headline verdict, per-account counts, and the
// full list of transactions it touched.
function LatestRun({ run }: { run: RecRun }) {
  const status = runStatus(run);
  const txs = flatten(run.results);

  return (
    <Card className="overflow-hidden">
      <CardContent className="p-0">
        <div className="p-6 pb-5">
          <div className="flex items-start justify-between gap-4 flex-wrap">
            <div className="flex items-baseline gap-3">
              {status.tone === "clean" ? (
                <CheckCircle2 className={`size-6 shrink-0 ${TONE_TEXT.clean}`} />
              ) : status.tone === "healed" ? (
                <ArrowDownToLine className={`size-6 shrink-0 ${TONE_TEXT.healed}`} />
              ) : (
                <AlertTriangle className={`size-6 shrink-0 ${TONE_TEXT.broken}`} />
              )}
              <div>
                <h2
                  className={`font-display text-[34px] leading-none font-light ${TONE_TEXT[status.tone]}`}
                >
                  {status.label}
                </h2>
                <p className="text-sm text-muted-foreground mt-2 max-w-prose leading-relaxed">
                  {status.blurb}
                </p>
              </div>
            </div>
            <p className="text-xs text-muted-foreground font-numeric text-right shrink-0">
              {TIME_FMT.format(new Date(run.ranAt))}
              <span className="block mt-0.5 uppercase tracking-wider">
                {run.trigger} · {run.window}
              </span>
            </p>
          </div>

          <div className="grid gap-3 sm:grid-cols-2 mt-6">
            {run.results.map((acc) => (
              <AccountTile key={acc.accountId} acc={acc} />
            ))}
          </div>
        </div>

        {txs.length > 0 && <TxLedger txs={txs} />}
      </CardContent>
    </Card>
  );
}

// Per-account counts with a hairline coverage bar — at a glance, how much of the
// window was already correct versus recovered.
function AccountTile({ acc }: { acc: RecAccountResult }) {
  const backfilled = acc.backfilledCount ?? 0;
  const total = acc.apiSettledCount;
  const gapPct = total > 0 ? Math.min(100, (acc.missingCount / total) * 100) : 0;

  return (
    <div className="rounded-lg border border-border bg-muted/25 px-4 py-3">
      <div className="flex items-baseline justify-between gap-2">
        <p className="eyebrow">{accountLabel(acc.accountType)}</p>
        {acc.error ? (
          <span className="text-xs text-destructive">failed</span>
        ) : acc.missingCount === 0 ? (
          <span className={`text-xs ${TONE_TEXT.clean}`}>complete</span>
        ) : (
          <span className={`text-xs font-numeric ${TONE_TEXT.healed}`}>+{backfilled} staged</span>
        )}
      </div>

      {acc.error ? (
        <p className="text-xs text-destructive mt-2 break-words">{acc.error}</p>
      ) : (
        <>
          <p className="font-numeric text-2xl font-light text-foreground mt-1.5 tabular-nums">
            {total}
            <span className="text-xs text-muted-foreground ml-1.5 font-sans">checked</span>
          </p>
          <div
            className="mt-2.5 h-[3px] rounded-full bg-border/70 overflow-hidden"
            role="presentation"
          >
            <div
              className="h-full rounded-full bg-[var(--signal)]"
              style={{ width: `${gapPct}%` }}
            />
          </div>
        </>
      )}
    </div>
  );
}

// The gaps themselves, as one chronological ledger: day headings carry the date,
// amounts are tabular and right-aligned so decimal points line up down the column.
function TxLedger({ txs }: { txs: FlatTx[] }) {
  const groups = groupByDay(txs);
  const anyUnrecovered = txs.some((tx) => tx.recovered === false);

  return (
    <div className="border-t border-border bg-muted/15">
      <div className="flex items-baseline justify-between gap-3 px-6 pt-4 pb-1">
        <p className="eyebrow">{anyUnrecovered ? "Gaps found" : "Recovered into staging"}</p>
        <p className="text-xs text-muted-foreground font-numeric">{txs.length}</p>
      </div>

      <div className="px-6 pb-5">
        {groups.map((group) => (
          <div key={group.key} className="mt-3 first:mt-1">
            <div className="flex items-center gap-3">
              <p className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground/80 shrink-0">
                {group.label}
              </p>
              <div className="divider-rule flex-1" />
            </div>

            <ul>
              {group.rows.map((tx) => (
                <li
                  key={tx.id}
                  className="flex items-center gap-3 py-[7px] border-b border-border/40 last:border-0 hover:bg-foreground/[0.03] -mx-2 px-2 rounded transition-colors"
                >
                  {tx.recovered === false ? (
                    <AlertTriangle className={`size-3.5 shrink-0 ${TONE_TEXT.broken}`} />
                  ) : (
                    <CheckCircle2 className={`size-3.5 shrink-0 ${TONE_TEXT.healed} opacity-70`} />
                  )}
                  <span className="text-sm text-foreground truncate flex-1" title={tx.description}>
                    {tx.description}
                  </span>
                  <span className="text-[11px] uppercase tracking-wider text-muted-foreground shrink-0">
                    {accountLabel(tx.accountType)}
                  </span>
                  <span className="font-numeric text-sm text-foreground/90 tabular-nums text-right w-[92px] shrink-0">
                    {fmtGbp(tx.amountPence)}
                  </span>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </div>
    </div>
  );
}

// History rows stay collapsed — one line each until you ask for the detail.
function EarlierRun({ run }: { run: RecRun }) {
  const status = runStatus(run);
  const txs = flatten(run.results);

  return (
    <Collapsible className="border-b border-border last:border-0">
      <CollapsibleTrigger className="group w-full flex items-center gap-3 px-4 py-2.5 text-left hover:bg-muted/40 transition-colors">
        <ChevronRight className="size-3.5 shrink-0 text-muted-foreground transition-transform group-data-[state=open]:rotate-90" />
        <span className="text-xs text-muted-foreground font-numeric shrink-0 w-[104px]">
          {TIME_FMT.format(new Date(run.ranAt))}
        </span>
        <span className="text-[11px] uppercase tracking-wider text-muted-foreground/70 shrink-0 w-14">
          {run.trigger}
        </span>
        <span className={`text-sm flex-1 truncate ${TONE_TEXT[status.tone]}`}>{status.label}</span>
        <span className="text-xs text-muted-foreground font-numeric shrink-0">
          {run.results.reduce((n, a) => n + a.apiSettledCount, 0)} checked
        </span>
      </CollapsibleTrigger>
      <CollapsibleContent>
        {txs.length === 0 ? (
          <p className="px-4 pb-3 pl-[46px] text-xs text-muted-foreground">
            Nothing missing in this run.
          </p>
        ) : (
          <TxLedger txs={txs} />
        )}
      </CollapsibleContent>
    </Collapsible>
  );
}
