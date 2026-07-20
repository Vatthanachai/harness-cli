using System.Collections.Concurrent;

using Microsoft.Data.Sqlite;

namespace Aiyara.Harness.Tools.Rag;

/// <summary>
/// A small pool of already-open, already-configured <see cref="SqliteConnection"/>s for one
/// connection string. Renting and returning a connection avoids paying to reopen (and, for
/// <see cref="SqliteVecVectorStore"/>, reload the native extension into) a fresh connection on
/// every <see cref="IVectorStore"/> call - measured as the dominant cost of that per-call-connection
/// design - while still giving genuinely concurrent callers (e.g. several agents searching at once)
/// distinct connections instead of serializing everyone through one.
/// </summary>
internal sealed class SqliteConnectionPool(string connectionString, Func<SqliteConnection, CancellationToken, Task> configureAsync)
{
    private readonly ConcurrentBag<SqliteConnection> _idle = new();

    public async Task<Rental> RentAsync(CancellationToken ct)
    {
        if (_idle.TryTake(out var connection)) return new Rental(this, connection);

        connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await configureAsync(connection, ct);
        return new Rental(this, connection);
    }

    /// <summary>
    /// A rented <see cref="SqliteConnection"/> - disposing it returns the connection to the pool
    /// (for a later rental to reuse) rather than closing it.
    /// </summary>
    public readonly struct Rental : IAsyncDisposable
    {
        private readonly SqliteConnectionPool _pool;

        internal Rental(SqliteConnectionPool pool, SqliteConnection connection)
        {
            _pool = pool;
            Connection = connection;
        }

        public SqliteConnection Connection { get; }

        public ValueTask DisposeAsync()
        {
            _pool._idle.Add(Connection);
            return ValueTask.CompletedTask;
        }
    }
}
