using Xunit;

// Test classes run one at a time.
//
// Not a performance concession — a correctness one. A FastEndpoints AppFixture
// instance is shared across every test class that uses it, and its SetupAsync
// runs once per class. Left parallel, one class's re-seed truncates the tables
// while another class's assertions are mid-flight, and the failures move around
// between runs.
//
// The whole suite is a few hundred milliseconds against LocalDB, so serialising
// it costs nothing worth measuring.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
