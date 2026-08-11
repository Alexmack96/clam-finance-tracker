namespace Clam.Api.Features.Monzo.GetMonzoStatus;

/// `Configured` and `Connected` are different failures and the card shows
/// different things for each: the first means nobody set the OAuth secrets, the
/// second means nobody has clicked Connect.
public sealed class GetMonzoStatusResponse
{
    public bool Configured { get; set; }
    public bool Connected { get; set; }
    public string? AccountId { get; set; }

    /// The newest staged transaction's timestamp, which is as close to "when did
    /// we last sync" as the data gets — there is no separate sync log.
    public DateTime? LastSyncedAt { get; set; }

    public int TotalStaged { get; set; }
}
