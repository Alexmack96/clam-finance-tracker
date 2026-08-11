using Clam.Api.Tests;
using Xunit;

// One host and one database for the whole project.
//
// Creating a LocalDB database and applying the schema costs about a second;
// doing it per test class would dominate the run. Assembly scope is also the
// only scope whose declared lifetime matches the real one — a class fixture is
// disposed when its first class finishes, which under a shared host tears it out
// from under every later class.
[assembly: AssemblyFixture(typeof(ClamApiFactory))]

// Test classes run one at a time.
//
// Not a performance concession, a correctness one: every test resets the
// database in its arrange step, and a reset landing mid-assertion in another
// class makes failures move around between runs.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
