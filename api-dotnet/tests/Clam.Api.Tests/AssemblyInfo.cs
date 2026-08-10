using Clam.Api.Tests;
using Xunit;

// Assembly-scoped fixtures, not class-scoped.
//
// This is the fix for a genuinely confusing failure mode. FastEndpoints boots
// ONE SUT per AppFixture type for the whole test project, but a class fixture's
// lifetime ends when its first test class finishes — so the shared host was
// being torn down while later classes were still using it. Symptom: every class
// passes on its own, and the suite fails as a whole with sub-millisecond errors
// because no HTTP is happening at all.
//
// Declaring them at assembly scope makes the declared lifetime match the real
// one: created once, disposed after the last test in the project.
[assembly: AssemblyFixture(typeof(ClamApp))]
[assembly: AssemblyFixture(typeof(ClamSeedApp))]

// Test classes run one at a time.
//
// Not a performance concession — a correctness one. The fixtures above are now
// shared project-wide, and POST /dev/seed truncates tables. Left parallel, that
// truncation lands mid-assertion in another class and the failures move around
// between runs.
//
// The whole suite is well under a second against LocalDB, so serialising it
// costs nothing worth measuring.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
