using Clam.Api.Domain;

namespace Clam.Api.Features.Dashboard;

/// The 50/30/20 Bucket arithmetic, kept free of any database type so it can be
/// unit-tested on its own.
public static class BucketMath
{
    /// Personal-view weighting: the viewer's own transactions count in full,
    /// Joint counts half (shared equally), everyone else's counts nothing.
    public static double OwnerWeight(Owner transactionOwner, Owner viewer)
    {
        if (transactionOwner == viewer) return 1;
        return transactionOwner == Owner.Joint ? 0.5 : 0;
    }

    /// Signed net for a single Bucket: expenses add, income (refunds) subtract,
    /// each owner-weighted. A £50 Wants refund therefore cancels £50 of Wants
    /// spend, or £25 if it was Joint.
    ///
    /// Callers pre-filter by month. Savings and Ignore are simply never passed
    /// as the target bucket for a spend gauge.
    public static double NetBucketSpent<T>(
        IEnumerable<T> transactions,
        Bucket bucket,
        Owner viewer,
        Func<T, (Owner Owner, TransactionType Type, double Amount, Bucket? Bucket)> project)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(project);

        var total = 0d;
        foreach (var item in transactions)
        {
            var (owner, type, amount, itemBucket) = project(item);
            if (itemBucket != bucket) continue;

            var weight = OwnerWeight(owner, viewer);
            if (weight == 0) continue;

            var signed = type == TransactionType.Expense ? amount : -amount;
            total += signed * weight;
        }
        return total;
    }
}
