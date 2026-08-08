using Clam.Api.Domain;

namespace Clam.Api.Features.Dev.SeedData;

/// The fixed set of categories the seeder creates, with the amount range and
/// bucket each one generates into.
///
/// The *names* are not invented. The dashboard and analytics routes look
/// categories up by name — "Net", "Salary", "Food &amp; Social", "Golf",
/// "Vacation" are all string literals in routes/dashboard.ts — so fake names
/// would make every aggregate return zero and prove nothing. The names are the
/// contract; the colours and every generated row are fabricated.
public sealed record SeedCategory(string Name, string Color, decimal Low, decimal High, Bucket Bucket);

public static class SeedCategoryCatalog
{
    public const string IncomeCategory = "Salary";
    public const string SettlementCategory = "Net";

    public static readonly SeedCategory[] All =
    [
        new("Activities",    "#f97316",    8m,   90m, Bucket.Wants),
        new("Charity",       "#14b8a6",   10m,   50m, Bucket.Wants),
        new("Education",     "#6366f1",   20m,  400m, Bucket.Needs),
        new("Fees",          "#94a3b8",    1m,   25m, Bucket.Needs),
        new("Food & Social", "#ec4899",   12m,  120m, Bucket.Wants),
        new("Fuel",          "#78716c",   30m,   85m, Bucket.Needs),
        new("Gifts",         "#f43f5e",   15m,  120m, Bucket.Wants),
        new("Golf",          "#22c55e",   25m,  150m, Bucket.Wants),
        new("Groceries",     "#84cc16",   15m,  140m, Bucket.Needs),
        new("Health",        "#06b6d4",   10m,  200m, Bucket.Needs),
        new("Home",          "#a855f7",   20m,  500m, Bucket.Needs),
        new("Insurance",     "#0ea5e9",   25m,  180m, Bucket.Needs),
        new("Investments",   "#10b981",  100m, 1200m, Bucket.Savings),
        new("Net",           "#3b82f6",  500m, 2600m, Bucket.Ignore),
        new("Pets",          "#eab308",   10m,   90m, Bucket.Needs),
        new("Rent",          "#ef4444",  900m, 1600m, Bucket.Needs),
        new("Salary",        "#16a34a", 2200m, 4200m, Bucket.Ignore),
        new("Savings",       "#059669",  100m,  900m, Bucket.Savings),
        new("Shopping",      "#d946ef",   15m,  300m, Bucket.Wants),
        new("Subscriptions", "#8b5cf6",    5m,   35m, Bucket.Wants),
        new("Takeout",       "#fb923c",   12m,   60m, Bucket.Wants),
        new("Transport",     "#0891b2",    3m,   45m, Bucket.Needs),
        new("Utilities",     "#facc15",   40m,  220m, Bucket.Needs),
        new("Vacation",      "#eab308",  120m, 1800m, Bucket.Wants),
        new("Work Lunch",    "#65a30d",    4m,   18m, Bucket.Wants),
    ];
}
