namespace Numera.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL advisory lock keys. Imports and the demo reset take <see cref="OrderWrites"/> for the length of their
/// transaction, so at most one of them changes the order data at a time.
/// </summary>
internal static class DataLocks
{
    public const long OrderWrites = 0x4E554D455241; // "NUMERA"

    /// <summary>Waits for the lock; released when the transaction ends.</summary>
    public const string WaitSql = "SELECT pg_advisory_xact_lock(@key)";

    /// <summary>Takes the lock only if it is free; returns false instead of waiting.</summary>
    public const string TrySql = "SELECT pg_try_advisory_xact_lock(@key)";
}
