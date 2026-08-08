namespace Clam.Api.Domain;

// These live outside Features/ on purpose, and they are the only types that do.
//
// A vertical slice owns its own request, response and data models because those
// are the slice's contract with the outside world, and letting slices share them
// is what turns a feature folder back into a layered app. These three are a
// different kind of thing: they are the database's contract. The exact strings
// are written into the CHECK constraints in db/schema.sql, so a slice cannot
// redefine them without lying about what the column accepts.
//
// Names must match the Prisma enum members exactly — they are what Dapper reads
// out of the NVARCHAR columns and what JsonStringEnumConverter writes back.

public enum TransactionType
{
    Income,
    Expense,
}

public enum Owner
{
    Alex,
    Casey,
    Joint,
}

public enum Bucket
{
    Needs,
    Wants,
    Savings,
    Ignore,
}
