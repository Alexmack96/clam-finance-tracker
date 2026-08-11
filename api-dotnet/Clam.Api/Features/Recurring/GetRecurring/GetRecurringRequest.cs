namespace Clam.Api.Features.Recurring.GetRecurring;

/// Bound from the query string. A string rather than the enum because an
/// unrecognised owner falls back to Alex here exactly as it does in Express,
/// instead of becoming a 400.
public sealed class GetRecurringRequest
{
    public string? Owner { get; set; }
}
