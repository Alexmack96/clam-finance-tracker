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
DROP TABLE IF EXISTS [Transaction];
DROP TABLE IF EXISTS [Category];

CREATE TABLE [Category] (
    [id]    NVARCHAR(30)  NOT NULL CONSTRAINT [PK_Category] PRIMARY KEY,
    [name]  NVARCHAR(100) NOT NULL,
    [color] NVARCHAR(20)  NOT NULL,
    CONSTRAINT [UQ_Category_name] UNIQUE ([name])
);

-- `Transaction` is a reserved T-SQL keyword, so it is bracketed everywhere.
-- Renaming it would break JSON shape parity with the Express API, which is the
-- whole point of this service, so the brackets stay.
CREATE TABLE [Transaction] (
    [id]               NVARCHAR(30)   NOT NULL CONSTRAINT [PK_Transaction] PRIMARY KEY,
    [description]      NVARCHAR(500)  NOT NULL,
    [amount]           DECIMAL(18, 2) NOT NULL,
    [type]             NVARCHAR(10)   NOT NULL,
    [date]             DATETIME2(3)   NOT NULL,
    [createdAt]        DATETIME2(3)   NOT NULL CONSTRAINT [DF_Transaction_createdAt] DEFAULT SYSUTCDATETIME(),
    [categoryId]       NVARCHAR(30)   NOT NULL,
    [externalId]       NVARCHAR(200)  NULL,
    [note]             NVARCHAR(MAX)  NULL,
    [owner]            NVARCHAR(10)   NOT NULL CONSTRAINT [DF_Transaction_owner] DEFAULT 'Joint',
    [reviewed]         BIT            NOT NULL CONSTRAINT [DF_Transaction_reviewed] DEFAULT 0,
    [bucket]           NVARCHAR(10)   NULL,
    [categoryPinned]   BIT            NOT NULL CONSTRAINT [DF_Transaction_categoryPinned] DEFAULT 0,
    [bucketPinned]     BIT            NOT NULL CONSTRAINT [DF_Transaction_bucketPinned] DEFAULT 0,
    [originalAmount]   DECIMAL(18, 2) NULL,
    [originalCurrency] NVARCHAR(10)   NULL,
    -- No FK yet: StatementFile is not part of this read-only slice. Kept as a
    -- plain column so the JSON shape still matches the Express response.
    [statementFileId]  NVARCHAR(30)   NULL,

    CONSTRAINT [FK_Transaction_Category] FOREIGN KEY ([categoryId])
        REFERENCES [Category] ([id]),

    -- The Prisma enums, enforced at the database instead of only in code. SQLite
    -- never checked these; SQL Server can, so it does.
    CONSTRAINT [CK_Transaction_type]   CHECK ([type] IN ('Income', 'Expense')),
    CONSTRAINT [CK_Transaction_owner]  CHECK ([owner] IN ('Alex', 'Casey', 'Joint')),
    CONSTRAINT [CK_Transaction_bucket] CHECK ([bucket] IN ('Needs', 'Wants', 'Savings', 'Ignore'))
);

-- `externalId` is `String? @unique` in Prisma. SQLite and Postgres treat every
-- NULL as distinct, so unlimited rows may have no external id. SQL Server does
-- NOT: a plain UNIQUE constraint permits exactly one NULL row and rejects the
-- second with a duplicate-key error. A filtered index restores the intended
-- semantics — unique among rows that actually have a value.
CREATE UNIQUE NONCLUSTERED INDEX [UQ_Transaction_externalId]
    ON [Transaction] ([externalId])
    WHERE [externalId] IS NOT NULL;

CREATE NONCLUSTERED INDEX [IX_Transaction_categoryId] ON [Transaction] ([categoryId]);
CREATE NONCLUSTERED INDEX [IX_Transaction_date]       ON [Transaction] ([date] DESC);
CREATE NONCLUSTERED INDEX [IX_Transaction_owner_type] ON [Transaction] ([owner], [type]);
