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
--      container.
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
DROP TABLE IF EXISTS [Transactions];
DROP TABLE IF EXISTS [Categories];

CREATE TABLE [Categories] (
    [id]    NVARCHAR(30)  NOT NULL CONSTRAINT [PK_Categories] PRIMARY KEY,
    [name]  NVARCHAR(100) NOT NULL,
    [color] NVARCHAR(20)  NOT NULL,
    CONSTRAINT [UQ_Categories_name] UNIQUE ([name])
);

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
    [id]               NVARCHAR(30)   NOT NULL CONSTRAINT [PK_Transactions] PRIMARY KEY,
    [description]      NVARCHAR(500)  NOT NULL,
    [amount]           DECIMAL(18, 2) NOT NULL,
    [type]             NVARCHAR(10)   NOT NULL,
    [date]             DATETIME2(3)   NOT NULL,
    [createdAt]        DATETIME2(3)   NOT NULL CONSTRAINT [DF_Transactions_createdAt] DEFAULT SYSUTCDATETIME(),
    [categoryId]       NVARCHAR(30)   NOT NULL,
    [externalId]       NVARCHAR(200)  NULL,
    [note]             NVARCHAR(MAX)  NULL,
    [owner]            NVARCHAR(10)   NOT NULL CONSTRAINT [DF_Transactions_owner] DEFAULT 'Joint',
    [reviewed]         BIT            NOT NULL CONSTRAINT [DF_Transactions_reviewed] DEFAULT 0,
    [bucket]           NVARCHAR(10)   NULL,
    [categoryPinned]   BIT            NOT NULL CONSTRAINT [DF_Transactions_categoryPinned] DEFAULT 0,
    [bucketPinned]     BIT            NOT NULL CONSTRAINT [DF_Transactions_bucketPinned] DEFAULT 0,
    [originalAmount]   DECIMAL(18, 2) NULL,
    [originalCurrency] NVARCHAR(10)   NULL,
    -- No FK yet: StatementFile is not part of this read-only slice. Kept as a
    -- plain column so the JSON shape still matches the Express response.
    [statementFileId]  NVARCHAR(30)   NULL,

    CONSTRAINT [FK_Transactions_Categories] FOREIGN KEY ([categoryId])
        REFERENCES [Categories] ([id]),

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
