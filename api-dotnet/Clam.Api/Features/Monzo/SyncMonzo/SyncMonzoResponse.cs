namespace Clam.Api.Features.Monzo.SyncMonzo;

public sealed class SyncReconciliation
{
    public string Window { get; set; } = "";
    public int Missing { get; set; }
    public int Backfilled { get; set; }
}

public sealed class SyncMonzoResponse
{
    /// Backfilled rows are staged imports too, so the headline count includes
    /// them — the user asked "how many new transactions", not "by which path".
    public int Imported { get; set; }

    public int Duplicates { get; set; }
    public SyncReconciliation Reconciled { get; set; } = new();
}
