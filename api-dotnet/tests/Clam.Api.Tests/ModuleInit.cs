using System.Runtime.CompilerServices;
using VerifyTests;

namespace Clam.Api.Tests;

/// Verify's one-time setup. A module initialiser rather than a fixture, because
/// these settings have to be in place before the first snapshot is compared and
/// nothing about them is per-test.
internal static class ModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        // Teaches Verify to render an HttpResponseMessage: status, headers and a
        // pretty-printed body. That rendering is the whole assertion — a test
        // asserts on the response, not on a deserialised DTO, because
        // deserialising is exactly what hides a wire-format regression.
        VerifyHttp.Initialize();

        // Snapshots live in one folder rather than beside each test file. There
        // are a few hundred of them and they are generated artefacts.
        Verifier.UseSourceFileRelativeDirectory("Snapshots");

        // Timestamps the *database* writes are the one thing the frozen clock
        // cannot pin: they come from SYSUTCDATETIME() defaults. Scrubbed by name
        // rather than by pattern, so the dates that carry meaning — a
        // transaction's `date`, a series' `nextDueDate` — stay visible and
        // asserted.
        //
        // Scrubbed by member name, not with a text scrubber. A text scrubber
        // cannot do this job: Verify invokes those per *value* during
        // serialisation, so the callback sees "Statements to chase" on its own
        // and never sees `createdAt: 2026-...Z` as one string. Any regex written
        // against `key: value` silently matches nothing and every one of these
        // timestamps reaches the snapshot as wall-clock.
        VerifierSettings.ScrubMembers(
            "createdAt",
            "updatedAt",
            "uploadedAt",
            "importedAt",
            "ranAt",
            "lastSyncedAt",
            // Also SYSUTCDATETIME(), not the frozen clock. Scrubbing keeps the
            // assertion that matters — null before settling, a value after.
            "settledAt");

        // A ProblemDetails carries the request's trace id, which is new every run.
        VerifierSettings.ScrubMember("traceId");

        // Verify's default date scrubbing would replace every date with
        // DateTime_1, DateTime_2 … including the seeded ones the assertions are
        // about. The clock is frozen, so dates are already deterministic.
        VerifierSettings.DontScrubDateTimes();
        VerifierSettings.DontScrubGuids();

        // Verify hides empty collections by default, which is wrong for an API
        // contract: `rows: []` and no `rows` key at all are different responses,
        // and the client only copes with one of them.
        VerifierSettings.DontIgnoreEmptyCollections();
    }
}
