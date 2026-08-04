-- Recurring-payment detection: store the human verdict, nothing else.
--
-- Cadence, amount, next-due, coverage and active state are all derived from
-- Transaction on every request. Persisting them here would create a second
-- source of truth that goes stale as soon as a statement is uploaded, so this
-- table holds only what cannot be recomputed: whether a person accepted or
-- rejected a detected series.
--
-- No row means "Proposed" — the absence of a decision is itself the state, so
-- the table stays at roughly one row per real subscription rather than one per
-- candidate.

CREATE TABLE "recurring_verdict" (
    "id"          TEXT NOT NULL PRIMARY KEY,
    "owner"       TEXT NOT NULL,
    "description" TEXT NOT NULL,
    "status"      TEXT NOT NULL,
    "note"        TEXT,
    "createdAt"   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAt"   DATETIME NOT NULL
);

-- Identity is (owner, description): exact description equality is what makes a
-- series group at all, so it is a sound key by construction.
CREATE UNIQUE INDEX "recurring_verdict_owner_description_key"
    ON "recurring_verdict"("owner", "description");
