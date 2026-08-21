-- Clam finance tracker — SQL Server schema.
--
-- Deliberately portable across all three targets:
--   * LocalDB   — (localdb)\MSSQLLocalDB, local development
--   * Azure SQL — the deployed database
--   * Testcontainers — mcr.microsoft.com/mssql/server, integration tests
--
-- Rules that keep it portable, do not break them:
--   1. No `GO`. That is a sqlcmd/SSMS batch separator, not T-SQL, and
--      SqlCommand.ExecuteNonQuery (which is how Testcontainers setup will run
--      this file) throws on it.
--   2. No `USE <db>` and no `CREATE DATABASE`. The caller picks the database;
--      a test container creates a throwaway one per run.
--   3. No filegroup, collation or file-path clauses — they differ per host.
--   4. Re-runnable from any state, so a test can reset without recreating the
--      container. That includes dropping tables this file no longer creates —
--      a retired table with a foreign key still blocks its parent's DROP.
--   5. An index or constraint naming a *newly added* column must be wrapped in
--      EXEC('...'). Rule 1 makes this one batch, so SQL Server compiles every
--      statement before running any of them. Where the table already exists
--      without that column the reference binds to the old table and fails at
--      compile time, which rejects the entire batch — the DROP and CREATE that
--      would have fixed it never execute. The error names the column and reads
--      exactly like a typo in this file. EXEC defers compilation to run time.
--
-- Applying it by hand needs sqlcmd's -I flag:
--     sqlcmd -S "(localdb)\MSSQLLocalDB" -E -d ClamDev -i schema.sql -I -b
-- sqlcmd is the one client that still defaults QUOTED_IDENTIFIER to OFF, and the
-- filtered index below requires it ON. Microsoft.Data.SqlClient turns it ON per
-- connection, so the C# and Testcontainers paths need no equivalent flag. Fixing
-- this with `SET QUOTED_IDENTIFIER ON` in the file would not work: the setting
-- applies at parse time, so it only affects *later* batches, and separating
-- batches means `GO`, which rule 1 rules out.

-- Dropped children-first: FK dependencies forbid the reverse order.
DROP TABLE IF EXISTS [RuleConditions];
DROP TABLE IF EXISTS [Rules];
DROP TABLE IF EXISTS [AmexTransactions];
DROP TABLE IF EXISTS [BarclaysTransactions];
DROP TABLE IF EXISTS [SantanderTransactions];
DROP TABLE IF EXISTS [HsbcTransactions];
DROP TABLE IF EXISTS [ChaseTransactions];
DROP TABLE IF EXISTS [SofiTransactions];
DROP TABLE IF EXISTS [MonzoApiTransactions];
DROP TABLE IF EXISTS [MonzoRecRuns];
DROP TABLE IF EXISTS [MonzoCredentials];
DROP TABLE IF EXISTS [InvestmentSnapshots];
DROP TABLE IF EXISTS [InvestmentAccounts];
DROP TABLE IF EXISTS [RecurringVerdicts];
DROP TABLE IF EXISTS [Notes];
DROP TABLE IF EXISTS [Tabs];
DROP TABLE IF EXISTS [Verifications];

-- Retired with Better Auth, and dropped here even though nothing below creates
-- them. Rule 4 says this file applies from any state, and a database that
-- predates WorkOS still has both — each with a foreign key onto [Users]. Without
-- these two lines the [Users] drop below fails on the constraint, the old table
-- survives, and the run dies further down on "Invalid column name
-- 'workOsUserId'" — which reads as a typo in the new schema rather than as a
-- table that refused to go. Delete them once no live database has these tables.
DROP TABLE IF EXISTS [Sessions];
DROP TABLE IF EXISTS [Accounts];

DROP TABLE IF EXISTS [Users];
DROP TABLE IF EXISTS [Transactions];
DROP TABLE IF EXISTS [StatementFiles];
DROP TABLE IF EXISTS [Categories];

-- [id] is NVARCHAR(50), not 30, and the same goes for every id and foreign key
-- in this file. Production ids are mostly 25-character cuids, but two categories
-- and both users carry 36-character UUIDs from an earlier scheme. At 30 those
-- rows cannot be migrated at all, and the error — String or binary data would be
-- truncated — names no column.
CREATE TABLE [Categories] (
    [id]    NVARCHAR(50)  NOT NULL CONSTRAINT [PK_Categories] PRIMARY KEY,
    [name]  NVARCHAR(100) NOT NULL,
    [color] NVARCHAR(20)  NOT NULL,
    CONSTRAINT [UQ_Categories_name] UNIQUE ([name])
);

-- The one row this schema ships with, because the application cannot run without
-- it and will not create it.
--
-- ProcessStagedCommand throws outright when it is missing ("the import pipeline
-- has nothing to fall back to"), DeleteCategory refuses to delete it and
-- MergeCategories refuses to merge it away. So every other piece of code treats
-- it as a permanent fixture while nothing anywhere brought it into existence —
-- which meant a database created from this file could stage an import and never
-- process one. The failure surfaced as an upload that appeared to succeed and
-- produced no transactions.
--
-- The id and colour are production's, so a later data migration matches this row
-- rather than colliding with it on the unique name.
INSERT INTO [Categories] ([id], [name], [color])
VALUES ('cmnk1z05a0004swu8j3pi8vb9', 'Uncategorised', '#d1d5db');

-- The source PDF a batch of staged rows came from. Created before [Transactions]
-- because that table points at it.
CREATE TABLE [StatementFiles] (
    [id]            NVARCHAR(50)  NOT NULL CONSTRAINT [PK_StatementFiles] PRIMARY KEY,
    [bank]          NVARCHAR(30)  NOT NULL,
    [owner]         NVARCHAR(10)  NOT NULL,
    [statementDate] NVARCHAR(40)  NULL,
    [originalName]  NVARCHAR(400) NOT NULL,
    -- SHA-256 of the file bytes, so a re-uploaded identical PDF is caught at the
    -- file level regardless of how row ids are derived.
    [contentHash]   NVARCHAR(64)  NOT NULL,
    [byteSize]      INT           NOT NULL,
    [storageKey]    NVARCHAR(400) NOT NULL,
    [uploadedAt]    DATETIME2(3)  NOT NULL CONSTRAINT [DF_StatementFiles_uploadedAt] DEFAULT SYSUTCDATETIME(),
    [rowCount]      INT           NULL,
    [reconciled]    BIT           NOT NULL CONSTRAINT [DF_StatementFiles_reconciled] DEFAULT 0,

    CONSTRAINT [UQ_StatementFiles_contentHash] UNIQUE ([contentHash])
);

CREATE NONCLUSTERED INDEX [IX_StatementFiles_bank_owner] ON [StatementFiles] ([bank], [owner]);

-- Table names are plural; column names are not.
--
-- The columns mirror Prisma's model fields exactly, because those names go on
-- the wire and JSON parity with the Express API is the point of this service.
-- Table names do not go on the wire, so they are free to follow SQL convention
-- instead. Pluralising also retires a real hazard: singular `Transaction` is a
-- reserved T-SQL keyword and only worked bracketed, one missed pair away from a
-- syntax error. `Transactions` is not reserved. Brackets are kept anyway, for
-- consistency with the quoted camelCase columns rather than out of necessity.
CREATE TABLE [Transactions] (
    [id]               NVARCHAR(50)   NOT NULL CONSTRAINT [PK_Transactions] PRIMARY KEY,
    [description]      NVARCHAR(500)  NOT NULL,
    [amount]           DECIMAL(18, 2) NOT NULL,
    [type]             NVARCHAR(10)   NOT NULL,
    [date]             DATETIME2(3)   NOT NULL,
    [createdAt]        DATETIME2(3)   NOT NULL CONSTRAINT [DF_Transactions_createdAt] DEFAULT SYSUTCDATETIME(),
    [categoryId]       NVARCHAR(50)   NOT NULL,
    [externalId]       NVARCHAR(200)  NULL,
    [note]             NVARCHAR(MAX)  NULL,
    [owner]            NVARCHAR(10)   NOT NULL CONSTRAINT [DF_Transactions_owner] DEFAULT 'Joint',
    [reviewed]         BIT            NOT NULL CONSTRAINT [DF_Transactions_reviewed] DEFAULT 0,
    [bucket]           NVARCHAR(10)   NULL,
    [originalAmount]   DECIMAL(18, 2) NULL,
    [originalCurrency] NVARCHAR(10)   NULL,
    [statementFileId]  NVARCHAR(50)   NULL,

    CONSTRAINT [FK_Transactions_Categories] FOREIGN KEY ([categoryId])
        REFERENCES [Categories] ([id]),

    -- SET NULL, not CASCADE, and that is the whole point: losing the source PDF
    -- must orphan the derived rows, never destroy them. Deleting a statement's
    -- transactions is a decision the delete slice makes on purpose.
    CONSTRAINT [FK_Transactions_StatementFiles] FOREIGN KEY ([statementFileId])
        REFERENCES [StatementFiles] ([id]) ON DELETE SET NULL,

    -- The Prisma enums, enforced at the database instead of only in code. SQLite
    -- never checked these; SQL Server can, so it does.
    CONSTRAINT [CK_Transactions_type]   CHECK ([type] IN ('Income', 'Expense')),
    CONSTRAINT [CK_Transactions_owner]  CHECK ([owner] IN ('Alex', 'Casey', 'Joint')),
    CONSTRAINT [CK_Transactions_bucket] CHECK ([bucket] IN ('Needs', 'Wants', 'Savings', 'Ignore'))
);

-- `externalId` is `String? @unique` in Prisma. SQLite and Postgres treat every
-- NULL as distinct, so unlimited rows may have no external id. SQL Server does
-- NOT: a plain UNIQUE constraint permits exactly one NULL row and rejects the
-- second with a duplicate-key error. A filtered index restores the intended
-- semantics — unique among rows that actually have a value.
CREATE UNIQUE NONCLUSTERED INDEX [UQ_Transactions_externalId]
    ON [Transactions] ([externalId])
    WHERE [externalId] IS NOT NULL;

CREATE NONCLUSTERED INDEX [IX_Transactions_categoryId] ON [Transactions] ([categoryId]);
CREATE NONCLUSTERED INDEX [IX_Transactions_date]       ON [Transactions] ([date] DESC);
CREATE NONCLUSTERED INDEX [IX_Transactions_owner_type] ON [Transactions] ([owner], [type]);
CREATE NONCLUSTERED INDEX [IX_Transactions_statementFileId] ON [Transactions] ([statementFileId]);

-- ─── Rules ───────────────────────────────────────────────────────────────────
--
-- Within a kind, [position] is the *only* thing that decides precedence: first
-- match wins, no specificity heuristic. Deliberately carries no uniqueness
-- constraint — a constraint is a thing that stops you creating a rule, and
-- duplicates are cheap to spot in the dry-run.

CREATE TABLE [Rules] (
    [id]           NVARCHAR(50) NOT NULL CONSTRAINT [PK_Rules] PRIMARY KEY,
    [kind]         NVARCHAR(10) NOT NULL,
    [position]     INT          NOT NULL,
    [joinOperator] NVARCHAR(3)  NOT NULL CONSTRAINT [DF_Rules_joinOperator] DEFAULT 'AND',
    [bank]         NVARCHAR(30) NULL,
    [categoryId]   NVARCHAR(50) NULL,
    [bucket]       NVARCHAR(10) NULL,
    [createdAt]    DATETIME2(3) NOT NULL CONSTRAINT [DF_Rules_createdAt] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [FK_Rules_Categories] FOREIGN KEY ([categoryId])
        REFERENCES [Categories] ([id]) ON DELETE CASCADE,

    CONSTRAINT [CK_Rules_kind]   CHECK ([kind] IN ('Category', 'Bucket')),
    CONSTRAINT [CK_Rules_join]   CHECK ([joinOperator] IN ('AND', 'OR')),
    CONSTRAINT [CK_Rules_bucket] CHECK ([bucket] IN ('Needs', 'Wants', 'Savings', 'Ignore'))
);

CREATE NONCLUSTERED INDEX [IX_Rules_kind_position] ON [Rules] ([kind], [position]);

CREATE TABLE [RuleConditions] (
    [id]       NVARCHAR(50)  NOT NULL CONSTRAINT [PK_RuleConditions] PRIMARY KEY,
    [ruleId]   NVARCHAR(50)  NOT NULL,
    [field]    NVARCHAR(20)  NOT NULL CONSTRAINT [DF_RuleConditions_field] DEFAULT 'Description',
    [operator] NVARCHAR(20)  NOT NULL CONSTRAINT [DF_RuleConditions_operator] DEFAULT 'Contains',
    [value]    NVARCHAR(200) NOT NULL,
    [negate]   BIT           NOT NULL CONSTRAINT [DF_RuleConditions_negate] DEFAULT 0,
    [position] INT           NOT NULL,

    CONSTRAINT [FK_RuleConditions_Rules] FOREIGN KEY ([ruleId])
        REFERENCES [Rules] ([id]) ON DELETE CASCADE,

    CONSTRAINT [CK_RuleConditions_field]    CHECK ([field] IN ('Description', 'Category', 'Type')),
    CONSTRAINT [CK_RuleConditions_operator] CHECK ([operator] IN ('Contains', 'StartsWith', 'EndsWith', 'Exact'))
);

CREATE NONCLUSTERED INDEX [IX_RuleConditions_ruleId] ON [RuleConditions] ([ruleId]);

-- ─── Bank staging ────────────────────────────────────────────────────────────
--
-- One table per bank, each preserving that bank's raw statement schema exactly.
-- The process slice normalises all of them into [Transactions]. Amounts stay
-- NVARCHAR here on purpose: staging holds what the statement said, including the
-- rows that will not parse, and the process step is where that becomes an error.

CREATE TABLE [MonzoApiTransactions] (
    [id]                NVARCHAR(50)  NOT NULL CONSTRAINT [PK_MonzoApiTransactions] PRIMARY KEY,
    [monzoId]           NVARCHAR(60)  NOT NULL,
    [created]           DATETIME2(3)  NOT NULL,
    [settled]           DATETIME2(3)  NULL,
    -- Pence, signed: negative is an expense. Kept as the API sends it so the
    -- process step owns the sign convention in one place.
    [amountPence]       INT           NOT NULL,
    [currency]          NVARCHAR(10)  NOT NULL,
    [localAmountPence]  INT           NOT NULL,
    [localCurrency]     NVARCHAR(10)  NOT NULL,
    [description]       NVARCHAR(500) NOT NULL,
    [notes]             NVARCHAR(MAX) NULL,
    [monzoCategory]     NVARCHAR(60)  NOT NULL,
    [merchantName]      NVARCHAR(300) NULL,
    [merchantEmoji]     NVARCHAR(20)  NULL,
    [merchantAddress]   NVARCHAR(400) NULL,
    [scheme]            NVARCHAR(60)  NULL,
    [includeInSpending] BIT           NOT NULL,
    [accountId]         NVARCHAR(60)  NOT NULL,
    [importedAt]        DATETIME2(3)  NOT NULL CONSTRAINT [DF_MonzoApiTransactions_importedAt] DEFAULT SYSUTCDATETIME(),
    [status]            NVARCHAR(20)  NOT NULL CONSTRAINT [DF_MonzoApiTransactions_status] DEFAULT 'pending',

    CONSTRAINT [UQ_MonzoApiTransactions_monzoId] UNIQUE ([monzoId])
);

CREATE NONCLUSTERED INDEX [IX_MonzoApiTransactions_status] ON [MonzoApiTransactions] ([status]);
CREATE NONCLUSTERED INDEX [IX_MonzoApiTransactions_accountId_created]
    ON [MonzoApiTransactions] ([accountId], [created] DESC);

CREATE TABLE [AmexTransactions] (
    [transactionId]   NVARCHAR(60)  NOT NULL CONSTRAINT [PK_AmexTransactions] PRIMARY KEY,
    [transactionDate] NVARCHAR(20)  NOT NULL,
    [processDate]     NVARCHAR(20)  NOT NULL,
    [description]     NVARCHAR(500) NOT NULL,
    [amount]          NVARCHAR(40)  NOT NULL,
    [isCredit]        BIT           NOT NULL CONSTRAINT [DF_AmexTransactions_isCredit] DEFAULT 0,
    -- Amex prints the currency's *name*, not its ISO code, and the name is what
    -- gets staged. "UNITED STATES DOLLAR" is exactly 20 characters, so 20 was a
    -- ceiling the very next currency would have hit.
    [foreignCurrency] NVARCHAR(60)  NULL,
    [foreignAmount]   NVARCHAR(40)  NULL,
    [statementDate]   NVARCHAR(40)  NOT NULL,
    [owner]           NVARCHAR(10)  NOT NULL CONSTRAINT [DF_AmexTransactions_owner] DEFAULT 'Alex',
    [importedAt]      DATETIME2(3)  NOT NULL CONSTRAINT [DF_AmexTransactions_importedAt] DEFAULT SYSUTCDATETIME(),
    [status]          NVARCHAR(20)  NOT NULL CONSTRAINT [DF_AmexTransactions_status] DEFAULT 'pending',
    -- Nullable: rows staged before statement tracking existed have no source file.
    [statementFileId] NVARCHAR(50)  NULL,

    CONSTRAINT [FK_AmexTransactions_StatementFiles] FOREIGN KEY ([statementFileId])
        REFERENCES [StatementFiles] ([id]) ON DELETE CASCADE
);

CREATE NONCLUSTERED INDEX [IX_AmexTransactions_statementFileId] ON [AmexTransactions] ([statementFileId]);
CREATE NONCLUSTERED INDEX [IX_AmexTransactions_status] ON [AmexTransactions] ([status]);

-- Barclays and Santander are the two banks whose statements carry no usable
-- per-row identifier, so [transactionId] is nullable and the process step falls
-- back to the surrogate [id]. Prisma used an autoincrement int for that; IDENTITY
-- is the same thing.
CREATE TABLE [BarclaysTransactions] (
    [id]            INT           NOT NULL IDENTITY(1,1) CONSTRAINT [PK_BarclaysTransactions] PRIMARY KEY,
    [transactionId] NVARCHAR(60)  NULL,
    [date]          NVARCHAR(20)  NOT NULL,
    [description]   NVARCHAR(500) NOT NULL,
    [amount]        NVARCHAR(40)  NOT NULL,
    [isCredit]      BIT           NOT NULL CONSTRAINT [DF_BarclaysTransactions_isCredit] DEFAULT 0,
    [statementDate] NVARCHAR(40)  NOT NULL,
    [owner]         NVARCHAR(10)  NOT NULL CONSTRAINT [DF_BarclaysTransactions_owner] DEFAULT 'Alex',
    [importedAt]    DATETIME2(3)  NOT NULL CONSTRAINT [DF_BarclaysTransactions_importedAt] DEFAULT SYSUTCDATETIME(),
    [status]        NVARCHAR(20)  NOT NULL CONSTRAINT [DF_BarclaysTransactions_status] DEFAULT 'pending'
);

CREATE UNIQUE NONCLUSTERED INDEX [UQ_BarclaysTransactions_transactionId]
    ON [BarclaysTransactions] ([transactionId])
    WHERE [transactionId] IS NOT NULL;

CREATE TABLE [SantanderTransactions] (
    [id]            INT           NOT NULL IDENTITY(1,1) CONSTRAINT [PK_SantanderTransactions] PRIMARY KEY,
    [transactionId] NVARCHAR(60)  NULL,
    [date]          NVARCHAR(20)  NOT NULL,
    [description]   NVARCHAR(500) NOT NULL,
    [moneyIn]       NVARCHAR(40)  NULL,
    [moneyOut]      NVARCHAR(40)  NULL,
    [balance]       NVARCHAR(40)  NOT NULL,
    [statementDate] NVARCHAR(40)  NOT NULL,
    [owner]         NVARCHAR(10)  NOT NULL CONSTRAINT [DF_SantanderTransactions_owner] DEFAULT 'Alex',
    [importedAt]    DATETIME2(3)  NOT NULL CONSTRAINT [DF_SantanderTransactions_importedAt] DEFAULT SYSUTCDATETIME(),
    [status]        NVARCHAR(20)  NOT NULL CONSTRAINT [DF_SantanderTransactions_status] DEFAULT 'pending'
);

CREATE UNIQUE NONCLUSTERED INDEX [UQ_SantanderTransactions_transactionId]
    ON [SantanderTransactions] ([transactionId])
    WHERE [transactionId] IS NOT NULL;

CREATE TABLE [HsbcTransactions] (
    [id]            INT           NOT NULL IDENTITY(1,1) CONSTRAINT [PK_HsbcTransactions] PRIMARY KEY,
    [transactionId] NVARCHAR(60)  NOT NULL,
    [date]          NVARCHAR(20)  NOT NULL,
    [paymentType]   NVARCHAR(60)  NOT NULL,
    [description]   NVARCHAR(500) NOT NULL,
    [moneyOut]      NVARCHAR(40)  NULL,
    [moneyIn]       NVARCHAR(40)  NULL,
    [balance]       NVARCHAR(40)  NULL,
    [statementDate] NVARCHAR(40)  NOT NULL,
    [owner]         NVARCHAR(10)  NOT NULL CONSTRAINT [DF_HsbcTransactions_owner] DEFAULT 'Joint',
    [importedAt]    DATETIME2(3)  NOT NULL CONSTRAINT [DF_HsbcTransactions_importedAt] DEFAULT SYSUTCDATETIME(),
    [status]        NVARCHAR(20)  NOT NULL CONSTRAINT [DF_HsbcTransactions_status] DEFAULT 'pending',
    -- Nullable: rows staged before statement tracking existed have no source file.
    [statementFileId] NVARCHAR(50) NULL,

    CONSTRAINT [UQ_HsbcTransactions_transactionId] UNIQUE ([transactionId]),
    CONSTRAINT [FK_HsbcTransactions_StatementFiles] FOREIGN KEY ([statementFileId])
        REFERENCES [StatementFiles] ([id]) ON DELETE CASCADE
);

CREATE NONCLUSTERED INDEX [IX_HsbcTransactions_statementFileId] ON [HsbcTransactions] ([statementFileId]);
CREATE NONCLUSTERED INDEX [IX_HsbcTransactions_status] ON [HsbcTransactions] ([status]);

CREATE TABLE [ChaseTransactions] (
    [id]            INT           NOT NULL IDENTITY(1,1) CONSTRAINT [PK_ChaseTransactions] PRIMARY KEY,
    [transactionId] NVARCHAR(60)  NOT NULL,
    [date]          NVARCHAR(20)  NOT NULL,
    [description]   NVARCHAR(500) NOT NULL,
    [amount]        NVARCHAR(40)  NOT NULL,
    [isCredit]      BIT           NOT NULL CONSTRAINT [DF_ChaseTransactions_isCredit] DEFAULT 0,
    [statementDate] NVARCHAR(40)  NOT NULL,
    [owner]         NVARCHAR(10)  NOT NULL CONSTRAINT [DF_ChaseTransactions_owner] DEFAULT 'Casey',
    [importedAt]    DATETIME2(3)  NOT NULL CONSTRAINT [DF_ChaseTransactions_importedAt] DEFAULT SYSUTCDATETIME(),
    [status]        NVARCHAR(20)  NOT NULL CONSTRAINT [DF_ChaseTransactions_status] DEFAULT 'pending',

    CONSTRAINT [UQ_ChaseTransactions_transactionId] UNIQUE ([transactionId])
);

CREATE TABLE [SofiTransactions] (
    [id]            INT           NOT NULL IDENTITY(1,1) CONSTRAINT [PK_SofiTransactions] PRIMARY KEY,
    [transactionId] NVARCHAR(60)  NOT NULL,
    [date]          NVARCHAR(20)  NOT NULL,
    [type]          NVARCHAR(60)  NOT NULL,
    [description]   NVARCHAR(500) NOT NULL,
    [amount]        NVARCHAR(40)  NOT NULL,
    [isCredit]      BIT           NOT NULL CONSTRAINT [DF_SofiTransactions_isCredit] DEFAULT 0,
    [balance]       NVARCHAR(40)  NULL,
    [accountType]   NVARCHAR(30)  NOT NULL CONSTRAINT [DF_SofiTransactions_accountType] DEFAULT 'Checking',
    [statementDate] NVARCHAR(40)  NOT NULL,
    [owner]         NVARCHAR(10)  NOT NULL CONSTRAINT [DF_SofiTransactions_owner] DEFAULT 'Casey',
    [importedAt]    DATETIME2(3)  NOT NULL CONSTRAINT [DF_SofiTransactions_importedAt] DEFAULT SYSUTCDATETIME(),
    [status]        NVARCHAR(20)  NOT NULL CONSTRAINT [DF_SofiTransactions_status] DEFAULT 'pending',

    CONSTRAINT [UQ_SofiTransactions_transactionId] UNIQUE ([transactionId])
);

-- ─── Monzo connection ────────────────────────────────────────────────────────

CREATE TABLE [MonzoCredentials] (
    [id]           NVARCHAR(50)  NOT NULL CONSTRAINT [PK_MonzoCredentials] PRIMARY KEY,
    [userId]       NVARCHAR(50)  NOT NULL,
    [accessToken]  NVARCHAR(MAX) NOT NULL,
    [refreshToken] NVARCHAR(MAX) NOT NULL,
    [expiresAt]    DATETIME2(3)  NOT NULL,
    [accountId]    NVARCHAR(60)  NULL,
    [createdAt]    DATETIME2(3)  NOT NULL CONSTRAINT [DF_MonzoCredentials_createdAt] DEFAULT SYSUTCDATETIME(),
    [updatedAt]    DATETIME2(3)  NOT NULL CONSTRAINT [DF_MonzoCredentials_updatedAt] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [UQ_MonzoCredentials_userId] UNIQUE ([userId])
);

-- One row per 90-day reconciliation pass. [results] is a JSON string: one entry
-- per synced account with its API-vs-staging diff.
CREATE TABLE [MonzoRecRuns] (
    [id]              NVARCHAR(50)  NOT NULL CONSTRAINT [PK_MonzoRecRuns] PRIMARY KEY,
    [ranAt]           DATETIME2(3)  NOT NULL CONSTRAINT [DF_MonzoRecRuns_ranAt] DEFAULT SYSUTCDATETIME(),
    [window]          NVARCHAR(10)  NOT NULL CONSTRAINT [DF_MonzoRecRuns_window] DEFAULT '90d',
    [trigger]         NVARCHAR(10)  NOT NULL CONSTRAINT [DF_MonzoRecRuns_trigger] DEFAULT 'sync',
    [totalMissing]    INT           NOT NULL CONSTRAINT [DF_MonzoRecRuns_totalMissing] DEFAULT 0,
    [totalBackfilled] INT           NOT NULL CONSTRAINT [DF_MonzoRecRuns_totalBackfilled] DEFAULT 0,
    [results]         NVARCHAR(MAX) NOT NULL
);

CREATE NONCLUSTERED INDEX [IX_MonzoRecRuns_ranAt] ON [MonzoRecRuns] ([ranAt] DESC);

-- ─── Standalone features ─────────────────────────────────────────────────────

CREATE TABLE [Notes] (
    [id]        NVARCHAR(50)  NOT NULL CONSTRAINT [PK_Notes] PRIMARY KEY,
    [title]     NVARCHAR(300) NOT NULL,
    [body]      NVARCHAR(MAX) NULL,
    [pinned]    BIT           NOT NULL CONSTRAINT [DF_Notes_pinned] DEFAULT 0,
    [createdAt] DATETIME2(3)  NOT NULL CONSTRAINT [DF_Notes_createdAt] DEFAULT SYSUTCDATETIME(),
    [updatedAt] DATETIME2(3)  NOT NULL CONSTRAINT [DF_Notes_updatedAt] DEFAULT SYSUTCDATETIME()
);

CREATE TABLE [Tabs] (
    [id]          NVARCHAR(50)   NOT NULL CONSTRAINT [PK_Tabs] PRIMARY KEY,
    [person]      NVARCHAR(200)  NOT NULL,
    [description] NVARCHAR(500)  NOT NULL,
    [amount]      DECIMAL(18, 2) NOT NULL,
    [direction]   NVARCHAR(10)   NOT NULL,
    [status]      NVARCHAR(10)   NOT NULL CONSTRAINT [DF_Tabs_status] DEFAULT 'Open',
    [dueDate]     DATETIME2(3)   NULL,
    [settledAt]   DATETIME2(3)   NULL,
    [note]        NVARCHAR(MAX)  NULL,
    [createdAt]   DATETIME2(3)   NOT NULL CONSTRAINT [DF_Tabs_createdAt] DEFAULT SYSUTCDATETIME(),
    [updatedAt]   DATETIME2(3)   NOT NULL CONSTRAINT [DF_Tabs_updatedAt] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [CK_Tabs_direction] CHECK ([direction] IN ('IOwe', 'TheyOwe')),
    CONSTRAINT [CK_Tabs_status]    CHECK ([status] IN ('Open', 'Settled'))
);

-- A human verdict on a detected recurring series, and *only* the verdict.
-- Cadence, amount and next-due are recomputed from transactions on every
-- request; the absence of a row is itself the "Proposed" state.
CREATE TABLE [RecurringVerdicts] (
    [id]          NVARCHAR(50)  NOT NULL CONSTRAINT [PK_RecurringVerdicts] PRIMARY KEY,
    [owner]       NVARCHAR(10)  NOT NULL,
    [description] NVARCHAR(400) NOT NULL,
    [status]      NVARCHAR(10)  NOT NULL,
    [note]        NVARCHAR(500) NULL,
    [createdAt]   DATETIME2(3)  NOT NULL CONSTRAINT [DF_RecurringVerdicts_createdAt] DEFAULT SYSUTCDATETIME(),
    [updatedAt]   DATETIME2(3)  NOT NULL CONSTRAINT [DF_RecurringVerdicts_updatedAt] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [UQ_RecurringVerdicts_owner_description] UNIQUE ([owner], [description]),
    CONSTRAINT [CK_RecurringVerdicts_owner]  CHECK ([owner] IN ('Alex', 'Casey', 'Joint')),
    CONSTRAINT [CK_RecurringVerdicts_status] CHECK ([status] IN ('Confirmed', 'Rejected'))
);

CREATE TABLE [InvestmentAccounts] (
    [id]        NVARCHAR(50)  NOT NULL CONSTRAINT [PK_InvestmentAccounts] PRIMARY KEY,
    [name]      NVARCHAR(200) NOT NULL,
    [category]  NVARCHAR(20)  NOT NULL,
    [owner]     NVARCHAR(10)  NOT NULL CONSTRAINT [DF_InvestmentAccounts_owner] DEFAULT 'Alex',
    [rate]      FLOAT         NULL,
    [sortOrder] INT           NOT NULL CONSTRAINT [DF_InvestmentAccounts_sortOrder] DEFAULT 0,
    [createdAt] DATETIME2(3)  NOT NULL CONSTRAINT [DF_InvestmentAccounts_createdAt] DEFAULT SYSUTCDATETIME(),
    [updatedAt] DATETIME2(3)  NOT NULL CONSTRAINT [DF_InvestmentAccounts_updatedAt] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [UQ_InvestmentAccounts_owner_name] UNIQUE ([owner], [name]),
    CONSTRAINT [CK_InvestmentAccounts_owner] CHECK ([owner] IN ('Alex', 'Casey', 'Joint')),
    CONSTRAINT [CK_InvestmentAccounts_category]
        CHECK ([category] IN ('pension', 'crypto', 'equity', 'cash', 'commodity', 'debt'))
);

CREATE TABLE [InvestmentSnapshots] (
    [id]        NVARCHAR(50) NOT NULL CONSTRAINT [PK_InvestmentSnapshots] PRIMARY KEY,
    [accountId] NVARCHAR(50) NOT NULL,
    [date]      DATETIME2(3) NOT NULL,
    [value]     FLOAT        NOT NULL,
    [createdAt] DATETIME2(3) NOT NULL CONSTRAINT [DF_InvestmentSnapshots_createdAt] DEFAULT SYSUTCDATETIME(),
    [updatedAt] DATETIME2(3) NOT NULL CONSTRAINT [DF_InvestmentSnapshots_updatedAt] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [FK_InvestmentSnapshots_InvestmentAccounts] FOREIGN KEY ([accountId])
        REFERENCES [InvestmentAccounts] ([id]) ON DELETE CASCADE,
    CONSTRAINT [UQ_InvestmentSnapshots_accountId_date] UNIQUE ([accountId], [date])
);

-- ─── Better Auth ─────────────────────────────────────────────────────────────
--
-- Owned by Better Auth on the Express side, so the column names are its schema,
-- not ours. This service only reads sessions and writes a user + credential row;
-- it never issues or rotates a session.

-- Identity lives in WorkOS. This table is the local mirror of it: the row that
-- says which of Alex and Casey a WorkOS login is, which nothing in a WorkOS
-- access token can tell us.
--
-- [workOsUserId] is the `sub` claim, and the only link between the two. It is
-- nullable because a row can exist before its person first signs in, and unique
-- through a filtered index so several such rows can wait at once.
CREATE TABLE [Users] (
    [id]           NVARCHAR(50)  NOT NULL CONSTRAINT [PK_Users] PRIMARY KEY,
    [workOsUserId] NVARCHAR(60)  NULL,
    [email]        NVARCHAR(320) NOT NULL,
    [name]         NVARCHAR(200) NOT NULL,
    [owner]        NVARCHAR(10)  NULL,
    [createdAt]    DATETIME2(3)  NOT NULL CONSTRAINT [DF_Users_createdAt] DEFAULT SYSUTCDATETIME(),
    [updatedAt]    DATETIME2(3)  NOT NULL CONSTRAINT [DF_Users_updatedAt] DEFAULT SYSUTCDATETIME(),
    [image]        NVARCHAR(500) NULL,

    CONSTRAINT [UQ_Users_email] UNIQUE ([email]),
    CONSTRAINT [CK_Users_owner] CHECK ([owner] IN ('Alex', 'Casey', 'Joint'))
);

-- Wrapped in EXEC, and it has to be. See rule 5 in the header: this file is one
-- batch, so every statement is compiled before any of them runs. Against a
-- database that already has a [Users] without this column, the index below binds
-- to *that* table at compile time, fails with "Invalid column name
-- 'workOsUserId'", and takes the whole batch down — including the DROP and
-- CREATE two lines up that would have fixed it. EXEC defers compilation to
-- execution, by which time the new table exists.
EXEC('
CREATE UNIQUE NONCLUSTERED INDEX [UQ_Users_workOsUserId]
    ON [Users] ([workOsUserId])
    WHERE [workOsUserId] IS NOT NULL;
');

-- Sessions and Accounts are gone with Better Auth. WorkOS holds the session and
-- the credential now, and this API never sees either: it validates a bearer
-- token against the WorkOS JWKS and reads [Users] for everything else.
--
-- Verifications stays, despite its name. It was Better Auth's generic
-- short-lived key/value store, and the Monzo OAuth slice borrows it to hold the
-- `state` parameter between /auth and /callback. That has nothing to do with
-- authenticating anyone here.
CREATE TABLE [Verifications] (
    [id]         NVARCHAR(60)  NOT NULL CONSTRAINT [PK_Verifications] PRIMARY KEY,
    [identifier] NVARCHAR(200) NOT NULL,
    [value]      NVARCHAR(MAX) NOT NULL,
    [expiresAt]  DATETIME2(3)  NOT NULL,
    [createdAt]  DATETIME2(3)  NOT NULL CONSTRAINT [DF_Verifications_createdAt] DEFAULT SYSUTCDATETIME(),
    [updatedAt]  DATETIME2(3)  NOT NULL CONSTRAINT [DF_Verifications_updatedAt] DEFAULT SYSUTCDATETIME()
);

CREATE NONCLUSTERED INDEX [IX_Verifications_identifier] ON [Verifications] ([identifier]);
