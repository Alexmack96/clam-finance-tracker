using System.Security.Cryptography;
using System.Text;

namespace Clam.Api.Infrastructure.Data;

public interface IIdGenerator
{
    string NewId();
}

/// Generates ids in Prisma's `cuid()` v1 shape.
///
/// Not cosmetic. Every id column is NVARCHAR(30) holding ids the Express service
/// minted, the client puts them in URLs, and existing rows are already this
/// shape — a GUID here would produce two visibly different kinds of id in the
/// same table depending on which service wrote the row. Collision-resistance is
/// the same property either way; matching the incumbent is the reason to prefer
/// this one.
///
/// Shape: 'c' + timestamp + counter(4) + fingerprint(4) + random(8), all base36,
/// 25 characters total.
///
/// An injected service rather than a static helper, because the timestamp makes
/// it a reader of the clock — and the clock is always an input, never something
/// code reaches for on its own.
public sealed class CuidGenerator(TimeProvider clock) : IIdGenerator
{
    private const int BlockSize = 4;
    private const int Base = 36;
    private const int Discrete = 1679616; // Base^BlockSize — the counter's wrap point.

    private static readonly string Fingerprint = BuildFingerprint();
    private static int _counter;

    public string NewId()
    {
        var timestamp = ToBase36(clock.GetUtcNow().ToUnixTimeMilliseconds());

        // Interlocked, not lock: two requests generating ids in the same
        // millisecond is the common case under any real load, and the counter is
        // the only thing separating them. It is also what keeps ids unique when
        // a test freezes the clock.
        var count = (int)((uint)Interlocked.Increment(ref _counter) % Discrete);

        return string.Concat(
            "c",
            timestamp,
            Pad(ToBase36(count), BlockSize),
            Fingerprint,
            RandomBlock(BlockSize * 2));
    }

    /// Distinguishes ids minted by two processes that start in the same
    /// millisecond with the same counter. Process id plus host name is what the
    /// reference implementation uses.
    private static string BuildFingerprint()
    {
        var pid = Pad(ToBase36(Environment.ProcessId), 2);

        var host = Environment.MachineName;
        var hostSum = host.Sum(c => (int)c) + host.Length + Base;
        var hostId = Pad(ToBase36(hostSum), 2);

        return pid[^2..] + hostId[^2..];
    }

    private static string RandomBlock(int length)
    {
        // The counter and timestamp make ids unique; this block makes them
        // unguessable, so it comes from the cryptographic generator rather than
        // Random.Shared.
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        var value = BitConverter.ToUInt64(bytes) % (ulong)Math.Pow(Base, length);
        return Pad(ToBase36((long)value), length);
    }

    private static string ToBase36(long value)
    {
        if (value == 0) return "0";

        const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
        var builder = new StringBuilder();
        while (value > 0)
        {
            builder.Insert(0, Alphabet[(int)(value % Base)]);
            value /= Base;
        }
        return builder.ToString();
    }

    private static string Pad(string value, int size) =>
        value.Length >= size ? value[^size..] : value.PadLeft(size, '0');
}
