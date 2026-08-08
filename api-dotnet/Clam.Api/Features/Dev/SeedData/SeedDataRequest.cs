namespace Clam.Api.Features.Dev.SeedData;

public sealed class SeedDataRequest
{
    /// How many transactions to generate. Categories are a fixed catalogue.
    public int Count { get; set; } = 1500;
}
