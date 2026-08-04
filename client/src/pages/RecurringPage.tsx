import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, ChevronRight, TriangleAlert, Undo2, X } from "lucide-react";
import {
  recurringTypeLabel,
  type Bucket,
  type RecurringCadence,
  type RecurringOwner,
  type RecurringStatus,
} from "@clam/core";
import api from "../lib/api.js";
import { Card, CardContent, CardHeader, CardTitle } from "../components/ui/card.js";
import { Button } from "../components/ui/button.js";
import { Skeleton } from "../components/ui/skeleton.js";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "../components/ui/select.js";
import { useSession } from "../lib/authClient.js";

const fmt = (n: number) =>
  new Intl.NumberFormat("en-GB", { style: "currency", currency: "GBP" }).format(n);

const fmtDate = (iso: string) =>
  new Date(iso).toLocaleDateString("en-GB", { day: "numeric", month: "short" });

interface Series {
  description: string;
  cadence: RecurringCadence;
  medianGapDays: number;
  occurrences: number;
  irregularity: number;
  coverage: number;
  lastDate: string;
  lastAmount: number;
  averageAmount: number;
  monthlyEquivalent: number;
  nextDueDate: string;
  bucket: Bucket | null;
  categoryName: string;
  bank: string | null;
  active: boolean;
  daysSinceLast: number;
  bankDataEndsAt: string | null;
  status: RecurringStatus | null;
  note: string | null;
}

interface RecurringResponse {
  owner: RecurringOwner;
  income: Series[];
  expense: Series[];
  committedOutPerMonth: number;
  committedInPerMonth: number;
}

const TYPE_STYLES: Record<string, string> = {
  Bill: "text-sky-600 dark:text-sky-400 border-sky-500/40",
  Subscription: "text-amber-600 dark:text-amber-400 border-amber-500/40",
  Investment: "text-emerald-600 dark:text-emerald-400 border-emerald-500/40",
  Transfer: "text-muted-foreground border-muted-foreground/40",
  Unclassified: "text-muted-foreground border-dashed border-muted-foreground/40",
};

export function RecurringPage() {
  const { data: session } = useSession();
  const [owner, setOwner] = useState<RecurringOwner>(
    session?.user.owner === "Casey" ? "Casey" : "Alex",
  );
  const [showRejected, setShowRejected] = useState(false);
  const queryClient = useQueryClient();

  const { data, isPending } = useQuery<RecurringResponse>({
    queryKey: ["recurring", owner],
    queryFn: () => api.get("/api/recurring", { params: { owner } }).then((r) => r.data),
  });

  const verdict = useMutation({
    mutationFn: (vars: { description: string; status: RecurringStatus | null }) =>
      api.put("/api/recurring/verdict", { owner, ...vars }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["recurring", owner] }),
  });

  const all = [...(data?.expense ?? []), ...(data?.income ?? [])];
  const proposed = all.filter((s) => s.status === null);
  const rejected = all.filter((s) => s.status === "Rejected");

  return (
    <div className="max-w-5xl mx-auto space-y-6">
      <header className="flex items-start justify-between gap-6">
        <div>
          <p className="eyebrow mb-3">Recurring</p>
          <h1 className="font-display text-[44px] leading-[1.02] font-light tracking-tight text-foreground">
            What leaves <span className="italic text-primary">every month</span>
          </h1>
          <p className="text-sm text-muted-foreground mt-2 max-w-xl">
            Detected from your transaction history by timing, not amount — so a variable energy bill
            counts and a fortnightly coffee habit doesn't.
          </p>
        </div>
        <Select value={owner} onValueChange={(v) => setOwner(v as RecurringOwner)}>
          <SelectTrigger className="h-8 w-[110px] text-xs shrink-0" aria-label="Select person">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="Alex">Alex</SelectItem>
            <SelectItem value="Casey">Casey</SelectItem>
          </SelectContent>
        </Select>
      </header>

      {/* Committed totals — confirmed items only, so the headline is something
          you vouched for rather than whatever the detector guessed. */}
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <Card className="surface-card-hover">
          <CardContent className="pt-6">
            <p className="eyebrow mb-3">Committed out</p>
            <p className="font-display text-[40px] leading-none font-light text-destructive/80">
              {isPending ? "—" : fmt(data?.committedOutPerMonth ?? 0)}
            </p>
            <p className="text-xs text-muted-foreground mt-3 font-numeric tracking-tight">
              PER MONTH · CONFIRMED ONLY
            </p>
          </CardContent>
        </Card>
        <Card className="surface-card-hover">
          <CardContent className="pt-6">
            <p className="eyebrow mb-3">Committed in</p>
            <p className="font-display text-[40px] leading-none font-light text-[var(--signal)]">
              {isPending ? "—" : fmt(data?.committedInPerMonth ?? 0)}
            </p>
            <p className="text-xs text-muted-foreground mt-3 font-numeric tracking-tight">
              PER MONTH · CONFIRMED ONLY
            </p>
          </CardContent>
        </Card>
      </div>

      {isPending ? (
        <Skeleton className="h-64 w-full rounded-xl" />
      ) : (
        <>
          {proposed.length > 0 && (
            <SeriesCard
              title={`Needs review (${proposed.length})`}
              blurb="Confirm the ones that are real. Rejected items collapse out of the way but can be brought back."
              rows={proposed}
              onVerdict={(description, status) => verdict.mutate({ description, status })}
              pending={verdict.isPending}
            />
          )}

          <SeriesCard
            title="Money out"
            blurb="Confirmed outgoings. Transfers and card payments are excluded from the total."
            rows={(data?.expense ?? []).filter((s) => s.status === "Confirmed")}
            onVerdict={(description, status) => verdict.mutate({ description, status })}
            pending={verdict.isPending}
            emptyHint="Nothing confirmed yet."
          />

          <SeriesCard
            title="Money in"
            blurb="Salary and any other income landing on a schedule."
            rows={(data?.income ?? []).filter((s) => s.status === "Confirmed")}
            onVerdict={(description, status) => verdict.mutate({ description, status })}
            pending={verdict.isPending}
            emptyHint="Nothing confirmed yet."
          />

          {rejected.length > 0 && (
            <Card>
              <CardHeader className="pb-2">
                <button
                  type="button"
                  onClick={() => setShowRejected((v) => !v)}
                  className="flex items-center gap-2 text-left"
                >
                  <ChevronRight
                    className={`h-3.5 w-3.5 text-muted-foreground transition-transform ${
                      showRejected ? "rotate-90" : ""
                    }`}
                  />
                  <CardTitle className="text-xs uppercase tracking-wide text-muted-foreground">
                    Rejected ({rejected.length})
                  </CardTitle>
                </button>
              </CardHeader>
              {showRejected && (
                <CardContent className="space-y-2">
                  {rejected.map((s) => (
                    <SeriesRow
                      key={s.description}
                      s={s}
                      onVerdict={(description, status) => verdict.mutate({ description, status })}
                      pending={verdict.isPending}
                    />
                  ))}
                </CardContent>
              )}
            </Card>
          )}
        </>
      )}
    </div>
  );
}

function SeriesCard({
  title,
  blurb,
  rows,
  onVerdict,
  pending,
  emptyHint,
}: {
  title: string;
  blurb: string;
  rows: Series[];
  onVerdict: (description: string, status: RecurringStatus | null) => void;
  pending: boolean;
  emptyHint?: string;
}) {
  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="text-base">{title}</CardTitle>
        <p className="text-xs text-muted-foreground mt-1">{blurb}</p>
      </CardHeader>
      <CardContent className="space-y-2">
        {rows.length === 0 ? (
          <p className="text-xs text-muted-foreground">{emptyHint ?? "Nothing here."}</p>
        ) : (
          rows.map((s) => (
            <SeriesRow key={s.description} s={s} onVerdict={onVerdict} pending={pending} />
          ))
        )}
      </CardContent>
    </Card>
  );
}

function SeriesRow({
  s,
  onVerdict,
  pending,
}: {
  s: Series;
  onVerdict: (description: string, status: RecurringStatus | null) => void;
  pending: boolean;
}) {
  const type = recurringTypeLabel(s.bucket);

  return (
    <div className="flex items-center gap-3 rounded-md border border-input bg-background px-3 py-2">
      <div className="flex-1 min-w-0">
        <div className="flex flex-wrap items-center gap-2">
          <span className="font-medium text-sm truncate max-w-[22rem]">{s.description.trim()}</span>
          <span
            className={`px-1.5 py-0.5 rounded-full text-[10px] font-medium border ${TYPE_STYLES[type]}`}
          >
            {type}
          </span>
          <span className="px-1.5 py-0.5 rounded-full text-[10px] border text-muted-foreground border-muted-foreground/40">
            {s.cadence}
          </span>
          {!s.active && (
            <span className="px-1.5 py-0.5 rounded-full text-[10px] border text-destructive border-destructive/40">
              stopped · {s.daysSinceLast}d
            </span>
          )}
          {/* "Active" is a claim about the last data we hold for this bank, not
              about today — say so rather than implying we know. */}
          {s.bankDataEndsAt && (
            <span
              className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded-full text-[10px] border text-amber-600 dark:text-amber-400 border-amber-500/40"
              title={`No ${s.bank ?? "bank"} data after this date, so status may be out of date`}
            >
              <TriangleAlert className="h-2.5 w-2.5" />
              {s.bank} to {fmtDate(s.bankDataEndsAt)}
            </span>
          )}
        </div>
        <p className="text-[11px] text-muted-foreground mt-1 font-numeric">
          {s.occurrences}× · every {s.medianGapDays}d · last {fmtDate(s.lastDate)}
          {s.active && <> · next ~{fmtDate(s.nextDueDate)}</>} · {s.categoryName}
        </p>
      </div>

      <div className="text-right shrink-0">
        <p className="font-numeric font-semibold text-sm">{fmt(s.averageAmount)}</p>
        {/* Only worth showing when the cadence isn't already monthly. */}
        {s.cadence !== "Monthly" && (
          <p className="text-[10px] text-muted-foreground font-numeric">
            {fmt(s.monthlyEquivalent)}/mo
          </p>
        )}
      </div>

      <div className="flex items-center gap-1 shrink-0">
        {s.status !== "Confirmed" && (
          <Button
            variant="ghost"
            size="icon"
            className="h-7 w-7 text-emerald-600 hover:text-emerald-600"
            title="Confirm — counts toward the total"
            disabled={pending}
            onClick={() => onVerdict(s.description, "Confirmed")}
          >
            <Check className="h-4 w-4" />
          </Button>
        )}
        {s.status === null && (
          <Button
            variant="ghost"
            size="icon"
            className="h-7 w-7 text-destructive hover:text-destructive"
            title="Not a real recurring payment"
            disabled={pending}
            onClick={() => onVerdict(s.description, "Rejected")}
          >
            <X className="h-4 w-4" />
          </Button>
        )}
        {s.status !== null && (
          <Button
            variant="ghost"
            size="icon"
            className="h-7 w-7 text-muted-foreground"
            title="Undo — back to needs review"
            disabled={pending}
            onClick={() => onVerdict(s.description, null)}
          >
            <Undo2 className="h-4 w-4" />
          </Button>
        )}
      </div>
    </div>
  );
}
