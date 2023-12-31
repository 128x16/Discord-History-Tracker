using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DHT.Server.Data;
using DHT.Server.Database.Repositories;
using DHT.Server.Database.Sqlite.Utils;
using Microsoft.Data.Sqlite;

namespace DHT.Server.Database.Sqlite.Repositories;

sealed class SqliteServerRepository : BaseSqliteRepository, IServerRepository {
	private readonly SqliteConnectionPool pool;

	public SqliteServerRepository(SqliteConnectionPool pool) {
		this.pool = pool;
	}

	public async Task Add(IReadOnlyList<Data.Server> servers) {
		using var conn = pool.Take();

		await using (var tx = await conn.BeginTransactionAsync()) {
			await using var cmd = conn.Upsert("servers", [
				("id", SqliteType.Integer),
				("name", SqliteType.Text),
				("type", SqliteType.Text)
			]);

			foreach (var server in servers) {
				cmd.Set(":id", server.Id);
				cmd.Set(":name", server.Name);
				cmd.Set(":type", ServerTypes.ToString(server.Type));
				await cmd.ExecuteNonQueryAsync();
			}

			await tx.CommitAsync();
		}

		UpdateTotalCount();
	}

	public override async Task<long> Count(CancellationToken cancellationToken) {
		using var conn = pool.Take();
		return await conn.ExecuteReaderAsync("SELECT COUNT(*) FROM servers", static reader => reader?.GetInt64(0) ?? 0L, cancellationToken);
	}

	public async IAsyncEnumerable<Data.Server> Get() {
		using var conn = pool.Take();

		await using var cmd = conn.Command("SELECT id, name, type FROM servers");
		await using var reader = await cmd.ExecuteReaderAsync();

		while (reader.Read()) {
			yield return new Data.Server {
				Id = reader.GetUint64(0),
				Name = reader.GetString(1),
				Type = ServerTypes.FromString(reader.GetString(2)),
			};
		}
	}
}