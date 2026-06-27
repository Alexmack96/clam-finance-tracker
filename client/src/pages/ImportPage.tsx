import { useEffect, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Upload, CheckCircle, AlertCircle, ChevronDown, ChevronUp } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "../components/ui/card.js";
import { Button } from "../components/ui/button.js";
import api from "../lib/api.js";

type ImportResult = { imported: number; duplicates?: string[] };
type ProcessResult = { processed: number; skipped: number; errored: number };

// Per-bank date (YYYY-MM-DD) of the most recent transaction, or null if none yet.
// `amexByOwner` breaks Amex down per owner since Alex and Casey share the card.
type LastStatement = {
  monzo: string | null;
  amex: string | null;
  barclays: string | null;
  santander: string | null;
  hsbc: string | null;
  sofi: string | null;
  chase: string | null;
  amexByOwner?: Record<string, string | null>;
};

// "2026-05-31" → "31 May 2026"
const fmtStatementDate = (iso: string | null | undefined) =>
  iso
    ? new Date(`${iso}T00:00:00`).toLocaleDateString("en-GB", {
        day: "numeric",
        month: "short",
        year: "numeric",
      })
    : null;

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
  owners = ["Alex", "Casey", "Joint"],
  onOwnerChange,
  lastStatement,
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
  owners?: string[];
  onOwnerChange?: (owner: string) => void;
  lastStatement?: string | null;
}) {
  const [showDuplicates, setShowDuplicates] = useState(false);
  const inputId = `file-${title.toLowerCase()}`;

  return (
    <Card>
      <CardHeader className="pb-2">
        <div className="flex items-center justify-between gap-3">
          <CardTitle className="text-xs uppercase tracking-wide text-muted-foreground">
            {title}
          </CardTitle>
          {lastStatement ? (
            <span className="inline-flex items-center gap-1.5 text-xs text-muted-foreground">
              <CheckCircle className="h-3.5 w-3.5 text-green-600 dark:text-green-400" />
              Statements through{" "}
              <span className="font-medium text-foreground font-numeric">
                {fmtStatementDate(lastStatement)}
              </span>
            </span>
          ) : (
            <span className="inline-flex items-center gap-1.5 text-xs font-medium text-amber-600 dark:text-amber-500">
              <AlertCircle className="h-3.5 w-3.5" />
              No statements uploaded yet
            </span>
          )}
        </div>
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
              {owners.map((o) => (
                <option key={o} value={o}>
                  {o}
                </option>
              ))}
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
              {result.imported.toLocaleString()} transactions imported
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
  // Amex is the only shared card (Alex + Casey), so it keeps an owner selector.
  // The rest belong to one person — fixed owner, no dropdown to misfile into.
  const [amexOwner, setAmexOwner] = useState("Alex");
  const barclaysOwner = "Alex";
  const santanderOwner = "Alex";
  const hsbcOwner = "Casey";
  const sofiOwner = "Casey";
  const chaseOwner = "Casey";

  const { data: lastStatement, refetch: refetchLastStatement } = useQuery<LastStatement>({
    queryKey: ["last-statement"],
    queryFn: () => api.get("/api/admin/last-statement").then((r) => r.data),
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
      refetchLastStatement();
    },
  });

  const monzoSyncMutation = useMutation<MonzoSyncResult, Error>({
    mutationFn: () => api.post("/api/admin/monzo/sync").then((r) => r.data),
    onSuccess: () => {
      refetchLastStatement();
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
      refetchLastStatement();
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
      refetchLastStatement();
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
      refetchLastStatement();
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
      refetchLastStatement();
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
      refetchLastStatement();
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
      refetchLastStatement();
      setSofiFile(null);
      if (sofiFileRef.current) sofiFileRef.current.value = "";
      processMutation.mutate();
    },
  });

  return (
    <div className="max-w-5xl mx-auto space-y-6">
      <div>
        <h1 className="text-3xl font-bold text-foreground">Import</h1>
        <p className="text-sm text-muted-foreground uppercase tracking-wide mt-1">
          Upload bank statements
        </p>
      </div>

      {/* Monzo — API-synced, not statement-based */}
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
                  <> · Last synced {new Date(monzoStatus.lastSyncedAt).toLocaleString()}</>
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
                  {monzoSyncMutation.data.imported.toLocaleString()} transactions synced
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
        owners={["Alex", "Casey"]}
        onOwnerChange={setAmexOwner}
        lastStatement={lastStatement?.amexByOwner?.[amexOwner] ?? null}
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
        lastStatement={lastStatement?.barclays}
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
        lastStatement={lastStatement?.santander}
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
        lastStatement={lastStatement?.hsbc}
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
        lastStatement={lastStatement?.chase}
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
        lastStatement={lastStatement?.sofi}
      />
    </div>
  );
}
