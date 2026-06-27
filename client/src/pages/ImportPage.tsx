import { useEffect, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Upload, CheckCircle, AlertCircle, ChevronDown, ChevronUp } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "../components/ui/card.js";
import { Button } from "../components/ui/button.js";
import api from "../lib/api.js";

type ImportResult = { imported: number; duplicates?: string[] };
type ProcessResult = { processed: number; skipped: number; errored: number };
type BankCounts = { pending: number; processed: number; skipped: number; errored: number };
type BankCountsWithOwner = BankCounts & { byOwner: Record<string, Record<string, number>> };
type StagedInfo = {
  monzo: BankCounts;
  amex: BankCountsWithOwner;
  barclays: BankCountsWithOwner;
  santander: BankCountsWithOwner;
  hsbc: BankCountsWithOwner;
  sofi: BankCountsWithOwner;
  chase: BankCountsWithOwner;
};

function BankUploadCard({
  title,
  description,
  onUpload,
  result,
  isPending,
  isError,
  error,
  onFileChange,
  file,
  fileRef,
  accept = ".csv",
  owner,
  onOwnerChange,
}: {
  title: string;
  description: string;
  onUpload: () => void;
  result: ImportResult | undefined;
  isPending: boolean;
  isError: boolean;
  error: Error | null;
  onFileChange: (f: File | null) => void;
  file: File | null;
  fileRef: React.RefObject<HTMLInputElement | null>;
  accept?: string;
  owner?: string;
  onOwnerChange?: (owner: string) => void;
}) {
  const [showDuplicates, setShowDuplicates] = useState(false);
  const inputId = `file-${title.toLowerCase()}`;

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-xs uppercase tracking-wide text-muted-foreground">
          {title}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <p className="text-sm text-muted-foreground">{description}</p>
        <div className="flex items-center gap-3">
          {onOwnerChange && (
            <select
              value={owner}
              onChange={(e) => onOwnerChange(e.target.value)}
              className="rounded-md border border-input bg-background px-3 py-2 text-sm"
            >
              <option value="Alex">Alex</option>
              <option value="Casey">Casey</option>
              <option value="Joint">Joint</option>
            </select>
          )}
          <label
            htmlFor={inputId}
            className="cursor-pointer inline-flex items-center gap-2 rounded-md border border-input bg-background px-3 py-2 text-sm hover:bg-accent hover:text-accent-foreground"
          >
            <Upload className="h-4 w-4" />
            {file ? file.name : "Choose file…"}
          </label>
          <input
            ref={fileRef}
            id={inputId}
            type="file"
            accept={accept}
            className="sr-only"
            onChange={(e) => {
              onFileChange(e.target.files?.[0] ?? null);
              setShowDuplicates(false);
            }}
          />
          <Button disabled={!file || isPending} onClick={onUpload}>
            {isPending ? "Syncing…" : "Upload & sync"}
          </Button>
        </div>

        {result && (
          <div className="space-y-2">
            <div className="flex items-center gap-2 text-sm text-green-600 dark:text-green-400">
              <CheckCircle className="h-4 w-4" />
              {result.imported.toLocaleString()} rows staged
              {(result.duplicates?.length ?? 0) > 0 && (
                <span className="text-muted-foreground">
                  · {result.duplicates!.length} already existed
                </span>
              )}
            </div>
            {(result.duplicates?.length ?? 0) > 0 && (
              <div>
                <button
                  className="flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground"
                  onClick={() => setShowDuplicates((v) => !v)}
                >
                  {showDuplicates ? (
                    <ChevronUp className="h-3 w-3" />
                  ) : (
                    <ChevronDown className="h-3 w-3" />
                  )}
                  {showDuplicates ? "Hide" : "Show"} duplicate IDs
                </button>
                {showDuplicates && (
                  <div className="mt-2 rounded-md border border-input bg-muted/50 p-3 max-h-48 overflow-y-auto">
                    {result.duplicates!.map((id) => (
                      <div key={id} className="font-mono text-xs text-muted-foreground">
                        {id}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            )}
          </div>
        )}

        {isError && (
          <div className="flex items-center gap-2 text-sm text-destructive">
            <AlertCircle className="h-4 w-4" />
            {error?.message}
          </div>
        )}
      </CardContent>
    </Card>
  );
}

type MonzoStatus = {
  configured: boolean;
  connected: boolean;
  accountId: string | null;
  lastSyncedAt: string | null;
  totalStaged: number;
};
type MonzoSyncResult = { imported: number; duplicates: number };

export function ImportPage() {
  const queryClient = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const amexFileRef = useRef<HTMLInputElement>(null);
  const barclaysFileRef = useRef<HTMLInputElement>(null);
  const santanderFileRef = useRef<HTMLInputElement>(null);
  const hsbcFileRef = useRef<HTMLInputElement>(null);
  const sofiFileRef = useRef<HTMLInputElement>(null);
  const chaseFileRef = useRef<HTMLInputElement>(null);
  const [amexFile, setAmexFile] = useState<File | null>(null);
  const [barclaysFile, setBarclaysFile] = useState<File | null>(null);
  const [santanderFile, setSantanderFile] = useState<File | null>(null);
  const [hsbcFile, setHsbcFile] = useState<File | null>(null);
  const [sofiFile, setSofiFile] = useState<File | null>(null);
  const [chaseFile, setChaseFile] = useState<File | null>(null);
  const [amexOwner, setAmexOwner] = useState("Alex");
  const [barclaysOwner, setBarclaysOwner] = useState("Alex");
  const [santanderOwner, setSantanderOwner] = useState("Alex");
  const [hsbcOwner, setHsbcOwner] = useState("Joint");
  const [sofiOwner, setSofiOwner] = useState("Casey");
  const [chaseOwner, setChaseOwner] = useState("Casey");

  const { data: staged, refetch: refetchStaged } = useQuery<StagedInfo>({
    queryKey: ["staged"],
    queryFn: () => api.get("/api/admin/staged").then((r) => r.data),
  });

  const { data: monzoStatus, refetch: refetchMonzoStatus } = useQuery<MonzoStatus>({
    queryKey: ["monzo-status"],
    queryFn: () => api.get("/api/admin/monzo/status").then((r) => r.data),
  });

  const processMutation = useMutation<ProcessResult, Error>({
    mutationFn: () => api.post("/api/admin/process").then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["transactions"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard"] });
      refetchStaged();
    },
  });

  const monzoSyncMutation = useMutation<MonzoSyncResult, Error>({
    mutationFn: () => api.post("/api/admin/monzo/sync").then((r) => r.data),
    onSuccess: () => {
      refetchStaged();
      refetchMonzoStatus();
      processMutation.mutate();
    },
  });

  const monzoDisconnectMutation = useMutation<void, Error>({
    mutationFn: () => api.post("/api/admin/monzo/disconnect").then((r) => r.data),
    onSuccess: () => refetchMonzoStatus(),
  });

  const monzoParam = searchParams.get("monzo");
  useEffect(() => {
    if (monzoParam) {
      setSearchParams({}, { replace: true });
      if (monzoParam === "connected") refetchMonzoStatus();
    }
  }, [monzoParam]);

  const amexMutation = useMutation<ImportResult, Error, { file: File; owner: string }>({
    mutationFn: ({ file, owner }) => {
      const form = new FormData();
      form.append("file", file);
      form.append("owner", owner);
      return api.post("/api/admin/import/amex", form).then((r) => r.data);
    },
    onSuccess: () => {
      refetchStaged();
      setAmexFile(null);
      if (amexFileRef.current) amexFileRef.current.value = "";
      processMutation.mutate();
    },
  });

  const barclaysMutation = useMutation<ImportResult, Error, { file: File; owner: string }>({
    mutationFn: ({ file, owner }) => {
      const form = new FormData();
      form.append("file", file);
      form.append("owner", owner);
      return api.post("/api/admin/import/barclays", form).then((r) => r.data);
    },
    onSuccess: () => {
      refetchStaged();
      setBarclaysFile(null);
      if (barclaysFileRef.current) barclaysFileRef.current.value = "";
      processMutation.mutate();
    },
  });

  const santanderMutation = useMutation<ImportResult, Error, { file: File; owner: string }>({
    mutationFn: ({ file, owner }) => {
      const form = new FormData();
      form.append("file", file);
      form.append("owner", owner);
      return api.post("/api/admin/import/santander", form).then((r) => r.data);
    },
    onSuccess: () => {
      refetchStaged();
      setSantanderFile(null);
      if (santanderFileRef.current) santanderFileRef.current.value = "";
      processMutation.mutate();
    },
  });

  const hsbcMutation = useMutation<ImportResult, Error, { file: File; owner: string }>({
    mutationFn: ({ file, owner }) => {
      const form = new FormData();
      form.append("file", file);
      form.append("owner", owner);
      return api.post("/api/admin/import/hsbc", form).then((r) => r.data);
    },
    onSuccess: () => {
      refetchStaged();
      setHsbcFile(null);
      if (hsbcFileRef.current) hsbcFileRef.current.value = "";
      processMutation.mutate();
    },
  });

  const chaseMutation = useMutation<ImportResult, Error, { file: File; owner: string }>({
    mutationFn: ({ file, owner }) => {
      const form = new FormData();
      form.append("file", file);
      form.append("owner", owner);
      return api.post("/api/admin/import/chase", form).then((r) => r.data);
    },
    onSuccess: () => {
      refetchStaged();
      setChaseFile(null);
      if (chaseFileRef.current) chaseFileRef.current.value = "";
      processMutation.mutate();
    },
  });

  const sofiMutation = useMutation<ImportResult, Error, { file: File; owner: string }>({
    mutationFn: ({ file, owner }) => {
      const form = new FormData();
      form.append("file", file);
      form.append("owner", owner);
      return api.post("/api/admin/import/sofi", form).then((r) => r.data);
    },
    onSuccess: () => {
      refetchStaged();
      setSofiFile(null);
      if (sofiFileRef.current) sofiFileRef.current.value = "";
      processMutation.mutate();
    },
  });

  const sum = (key: keyof BankCounts) =>
    (staged?.monzo[key] ?? 0) +
    (staged?.amex[key] ?? 0) +
    (staged?.barclays[key] ?? 0) +
    (staged?.santander[key] ?? 0) +
    (staged?.hsbc[key] ?? 0) +
    (staged?.sofi[key] ?? 0) +
    (staged?.chase[key] ?? 0);
  const totalPending = sum("pending");
  const totalProcessed = sum("processed");
  const totalErrored = sum("errored");
  const totalStaged = totalPending + totalProcessed + sum("skipped") + totalErrored;

  return (
    <div className="max-w-5xl mx-auto space-y-6">
      <div>
        <h1 className="text-3xl font-bold text-foreground">Import</h1>
        <p className="text-sm text-muted-foreground uppercase tracking-wide mt-1">
          Upload bank statements
        </p>
      </div>

      {/* Staging status */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-xs uppercase tracking-wide text-muted-foreground">
            Staging
          </CardTitle>
        </CardHeader>
        <CardContent>
          {totalStaged === 0 ? (
            <p className="text-sm text-muted-foreground">No staged transactions yet.</p>
          ) : (
            <div className="space-y-3">
              {/* Per-bank breakdown */}
              <div className="flex flex-wrap gap-6 text-sm">
                {(["monzo", "amex", "barclays", "santander", "hsbc", "sofi", "chase"] as const).map(
                  (bank) => {
                    const counts = staged?.[bank];
                    if (!counts) return null;
                    const total = counts.pending + counts.processed + counts.skipped;
                    if (total === 0) return null;
                    const ownerCounts = "byOwner" in counts ? counts.byOwner : null;
                    const owners = ownerCounts
                      ? Object.entries(ownerCounts)
                          .map(
                            ([o, s]) =>
                              `${o} ${((s.pending ?? 0) + (s.processed ?? 0) + (s.skipped ?? 0)).toLocaleString()}`,
                          )
                          .join(" · ")
                      : null;
                    return (
                      <div key={bank}>
                        <span className="font-medium text-foreground capitalize">{bank}</span>
                        <span className="text-muted-foreground ml-1">
                          {total.toLocaleString()} rows
                        </span>
                        {owners && (
                          <span className="text-muted-foreground/60 ml-1 text-xs">({owners})</span>
                        )}
                      </div>
                    );
                  },
                )}
              </div>
              {/* Status summary + (fallback) action */}
              <div className="flex items-center gap-4">
                <span className="text-sm text-muted-foreground">
                  {totalProcessed.toLocaleString()} processed
                  {totalErrored > 0 && (
                    <>
                      {" "}
                      ·{" "}
                      <span className="text-destructive">
                        {totalErrored.toLocaleString()} errored
                      </span>
                    </>
                  )}
                  {totalPending > 0 && (
                    <>
                      {" "}
                      ·{" "}
                      <span className="text-foreground font-medium">
                        {totalPending.toLocaleString()} pending
                      </span>
                    </>
                  )}
                  {processMutation.isPending && (
                    <>
                      {" "}
                      · <span className="text-foreground">syncing…</span>
                    </>
                  )}
                </span>
                {totalPending > 0 && (
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={processMutation.isPending}
                    onClick={() => processMutation.mutate()}
                  >
                    {processMutation.isPending ? "Processing…" : "Re-process pending"}
                  </Button>
                )}
              </div>
              {processMutation.isSuccess && (
                <div className="flex items-center gap-2 text-sm text-green-600 dark:text-green-400">
                  <CheckCircle className="h-4 w-4" />
                  {processMutation.data.processed.toLocaleString()} transactions added
                  {processMutation.data.errored > 0 && (
                    <span className="text-destructive">
                      · {processMutation.data.errored.toLocaleString()} errored
                    </span>
                  )}
                </div>
              )}
            </div>
          )}
        </CardContent>
      </Card>

      {/* Active bank upload cards */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-xs uppercase tracking-wide text-muted-foreground">
            Monzo
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          {monzoParam === "error" && (
            <div className="flex items-center gap-2 text-sm text-destructive">
              <AlertCircle className="h-4 w-4" />
              Connection failed — check your client credentials and try again.
            </div>
          )}
          {!monzoStatus?.configured ? (
            <p className="text-sm text-muted-foreground">
              Set <span className="font-mono">MONZO_CLIENT_ID</span>,{" "}
              <span className="font-mono">MONZO_CLIENT_SECRET</span>, and{" "}
              <span className="font-mono">MONZO_REDIRECT_URI</span> in{" "}
              <span className="font-mono">server/.env</span>.
            </p>
          ) : !monzoStatus.connected ? (
            <>
              <p className="text-sm text-muted-foreground">
                Connect your Monzo account — tokens refresh automatically, no more manual
                copy-paste.
              </p>
              <a href="/api/admin/monzo/auth">
                <Button>Connect Monzo</Button>
              </a>
            </>
          ) : (
            <>
              <p className="text-sm text-muted-foreground">
                Connected · <span className="font-mono text-xs">{monzoStatus.accountId}</span>
                {monzoStatus.lastSyncedAt && (
                  <>
                    {" "}
                    · Last synced {new Date(monzoStatus.lastSyncedAt).toLocaleString()} ·{" "}
                    {monzoStatus.totalStaged.toLocaleString()} staged
                  </>
                )}
              </p>
              <div className="flex items-center gap-3">
                <Button
                  disabled={monzoSyncMutation.isPending}
                  onClick={() => monzoSyncMutation.mutate()}
                >
                  {monzoSyncMutation.isPending ? "Syncing…" : "Sync now"}
                </Button>
                <Button
                  variant="outline"
                  disabled={monzoDisconnectMutation.isPending}
                  onClick={() => monzoDisconnectMutation.mutate()}
                >
                  Disconnect
                </Button>
              </div>
              {monzoSyncMutation.isSuccess && (
                <div className="flex items-center gap-2 text-sm text-green-600 dark:text-green-400">
                  <CheckCircle className="h-4 w-4" />
                  {monzoSyncMutation.data.imported.toLocaleString()} rows staged
                  {monzoSyncMutation.data.duplicates > 0 && (
                    <span className="text-muted-foreground">
                      · {monzoSyncMutation.data.duplicates.toLocaleString()} already existed
                    </span>
                  )}
                </div>
              )}
              {monzoSyncMutation.isError && (
                <div className="flex items-center gap-2 text-sm text-destructive">
                  <AlertCircle className="h-4 w-4" />
                  {monzoSyncMutation.error.message}
                </div>
              )}
            </>
          )}
        </CardContent>
      </Card>

      <BankUploadCard
        title="Amex"
        description="Download from Amex online → Statements → View/Download PDF."
        accept=".pdf,application/pdf"
        file={amexFile}
        fileRef={amexFileRef}
        onFileChange={(f) => {
          setAmexFile(f);
          amexMutation.reset();
        }}
        onUpload={() => amexFile && amexMutation.mutate({ file: amexFile, owner: amexOwner })}
        result={amexMutation.data}
        isPending={amexMutation.isPending || (amexMutation.isSuccess && processMutation.isPending)}
        isError={amexMutation.isError}
        error={amexMutation.error}
        owner={amexOwner}
        onOwnerChange={setAmexOwner}
      />

      <BankUploadCard
        title="Barclays"
        description="Download from Barclays online → Statements → View statement → Save as PDF."
        accept=".pdf,application/pdf"
        file={barclaysFile}
        fileRef={barclaysFileRef}
        onFileChange={(f) => {
          setBarclaysFile(f);
          barclaysMutation.reset();
        }}
        onUpload={() =>
          barclaysFile && barclaysMutation.mutate({ file: barclaysFile, owner: barclaysOwner })
        }
        result={barclaysMutation.data}
        isPending={
          barclaysMutation.isPending || (barclaysMutation.isSuccess && processMutation.isPending)
        }
        isError={barclaysMutation.isError}
        error={barclaysMutation.error}
        owner={barclaysOwner}
        onOwnerChange={setBarclaysOwner}
      />

      <BankUploadCard
        title="Santander"
        description="Download from Santander online → My Accounts → Statements → Download PDF."
        accept=".pdf,application/pdf"
        file={santanderFile}
        fileRef={santanderFileRef}
        onFileChange={(f) => {
          setSantanderFile(f);
          santanderMutation.reset();
        }}
        onUpload={() =>
          santanderFile && santanderMutation.mutate({ file: santanderFile, owner: santanderOwner })
        }
        result={santanderMutation.data}
        isPending={
          santanderMutation.isPending || (santanderMutation.isSuccess && processMutation.isPending)
        }
        isError={santanderMutation.isError}
        error={santanderMutation.error}
        owner={santanderOwner}
        onOwnerChange={setSantanderOwner}
      />

      <BankUploadCard
        title="HSBC"
        description="Download from HSBC online → My accounts → Statements → View statement → Print/Save as PDF."
        accept=".pdf,application/pdf"
        file={hsbcFile}
        fileRef={hsbcFileRef}
        onFileChange={(f) => {
          setHsbcFile(f);
          hsbcMutation.reset();
        }}
        onUpload={() => hsbcFile && hsbcMutation.mutate({ file: hsbcFile, owner: hsbcOwner })}
        result={hsbcMutation.data}
        isPending={hsbcMutation.isPending || (hsbcMutation.isSuccess && processMutation.isPending)}
        isError={hsbcMutation.isError}
        error={hsbcMutation.error}
        owner={hsbcOwner}
        onOwnerChange={setHsbcOwner}
      />

      <BankUploadCard
        title="Chase"
        description="Download from Chase online → Statements → View statement → Save as PDF."
        accept=".pdf,application/pdf"
        file={chaseFile}
        fileRef={chaseFileRef}
        onFileChange={(f) => {
          setChaseFile(f);
          chaseMutation.reset();
        }}
        onUpload={() => chaseFile && chaseMutation.mutate({ file: chaseFile, owner: chaseOwner })}
        result={chaseMutation.data}
        isPending={
          chaseMutation.isPending || (chaseMutation.isSuccess && processMutation.isPending)
        }
        isError={chaseMutation.isError}
        error={chaseMutation.error}
        owner={chaseOwner}
        onOwnerChange={setChaseOwner}
      />

      <BankUploadCard
        title="SoFi"
        description="Download from SoFi app → Account → Statements → Download PDF. Imports both Checking and Savings transactions."
        accept=".pdf,application/pdf"
        file={sofiFile}
        fileRef={sofiFileRef}
        onFileChange={(f) => {
          setSofiFile(f);
          sofiMutation.reset();
        }}
        onUpload={() => sofiFile && sofiMutation.mutate({ file: sofiFile, owner: sofiOwner })}
        result={sofiMutation.data}
        isPending={sofiMutation.isPending || (sofiMutation.isSuccess && processMutation.isPending)}
        isError={sofiMutation.isError}
        error={sofiMutation.error}
        owner={sofiOwner}
        onOwnerChange={setSofiOwner}
      />
    </div>
  );
}
