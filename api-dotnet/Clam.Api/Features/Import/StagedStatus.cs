namespace Clam.Api.Features.Import;

/// The lifecycle of a staged row, as the `status` column spells it. Lowercase
/// strings rather than a C# enum because they are already in every staging
/// table and on the wire, and casing them differently here would mean a
/// translation layer that exists purely to be prettier.
internal static class StagedStatus
{
    internal const string Pending = "pending";
    internal const string Processed = "processed";
    internal const string Skipped = "skipped";
    internal const string Errored = "errored";
}

/// The banks that stage rows, and the `externalId` namespace each one owns.
/// Feature-shared (tier 2): the staged-count, last-statement and process slices
/// all enumerate the same set, and a bank that appears in two of the three is
/// exactly the bug this prevents.
internal static class StagingBanks
{
    /// Namespace → the table its staged rows live in. `flex` is absent
    /// deliberately: Monzo Flex shares the Monzo staging table and is split out
    /// by account id at process time.
    internal static readonly (string Bank, string Table)[] All =
    [
        ("monzo", "MonzoApiTransactions"),
        ("amex", "AmexTransactions"),
        ("barclays", "BarclaysTransactions"),
        ("santander", "SantanderTransactions"),
        ("hsbc", "HsbcTransactions"),
        ("sofi", "SofiTransactions"),
        ("chase", "ChaseTransactions"),
    ];
}
