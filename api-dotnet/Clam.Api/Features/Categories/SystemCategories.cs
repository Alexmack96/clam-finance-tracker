using Clam.Api.Domain;

namespace Clam.Api.Features.Categories;

/// The categories a database is expected to come up with, and the bucket each
/// one starts in.
///
/// Ported from the Express service's <c>initSystemCategories</c>, names and
/// colours unchanged, because these are the names the existing production data
/// already uses. Renaming one here would leave the migrated rows pointing at a
/// category this no longer creates.
///
/// **Uncategorised is deliberately unbucketed.** It is the fallback for a
/// transaction no rule matched, and giving it a bucket would silently stamp
/// every un-ruled transaction as Wants — which is a number on the dashboard that
/// looks like a decision somebody made.
public sealed record SystemCategory(string Name, string Color, Bucket? Bucket);

public static class SystemCategories
{
    /// <c>Uncategorised</c> is in this list and is also inserted by
    /// <c>db/schema.sql</c>, with production's own id. That is not a conflict:
    /// the seeder skips a category that already exists, so the schema's row
    /// wins and keeps its id. It stays listed so the set is readable as a whole
    /// rather than being one name short for a reason you have to go and find.
    public static readonly SystemCategory[] All =
    [
        new("Activities",    "#8b5cf6", Domain.Bucket.Wants),
        new("Bank Sauce",    "#0ea5e9", Domain.Bucket.Wants),
        new("Entertainment", "#7C3AED", Domain.Bucket.Wants),
        new("Food & Social", "#fb923c", Domain.Bucket.Wants),
        new("Groceries",     "#22c55e", Domain.Bucket.Wants),
        new("Takeout",       "#ef4444", Domain.Bucket.Wants),
        new("Personal Care", "#f43f5e", Domain.Bucket.Wants),
        new("Rent & Bills",  "#64748b", Domain.Bucket.Needs),
        new("Savings",       "#a855f7", Domain.Bucket.Savings),
        new("Transport",     "#3b82f6", Domain.Bucket.Needs),
        new("Uncategorised", "#d1d5db", null),
        new("Vacation",      "#eab308", Domain.Bucket.Wants),
    ];
}
