using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace ActivityLedger.Core;

public sealed class ActivityStore : IDisposable
{
    private readonly object gate = new();
    private readonly SqliteConnection connection;

    public ActivityStore(string dbPath)
    {
        if (dbPath != ":memory:") Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
        connection = new(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA busy_timeout=5000;
            PRAGMA secure_delete=ON;
            CREATE TABLE IF NOT EXISTS activity (
                id TEXT PRIMARY KEY,
                start_utc INTEGER NOT NULL,
                end_utc INTEGER NOT NULL,
                kind TEXT NOT NULL,
                process_name TEXT NOT NULL,
                title TEXT NOT NULL,
                url TEXT NOT NULL,
                domain TEXT NOT NULL,
                CHECK(end_utc >= start_utc)
            );
            CREATE INDEX IF NOT EXISTS idx_activity_time ON activity(start_utc, end_utc);
            CREATE INDEX IF NOT EXISTS idx_activity_end ON activity(end_utc);
            """;
        command.ExecuteNonQuery();
    }

    public void Upsert(ActivityRecord record)
    {
        if (record.EndUtc < record.StartUtc) throw new ArgumentException("A session cannot end before it starts.", nameof(record));
        lock (gate) UpsertInternal(record, null);
    }

    private void UpsertInternal(ActivityRecord record, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO activity(id,start_utc,end_utc,kind,process_name,title,url,domain)
            VALUES($id,$start,$end,$kind,$process,$title,$url,$domain)
            ON CONFLICT(id) DO UPDATE SET end_utc=excluded.end_utc;
            """;
        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$start", record.StartUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$end", record.EndUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$kind", record.Kind);
        command.Parameters.AddWithValue("$process", record.ProcessName);
        command.Parameters.AddWithValue("$title", record.Title);
        command.Parameters.AddWithValue("$url", record.Url);
        command.Parameters.AddWithValue("$domain", record.Domain);
        command.ExecuteNonQuery();
    }

    /// <summary>Returns newest first, clipped to [fromUtc,toUtc). Use int.MaxValue to export all matches.</summary>
    public IReadOnlyList<ActivityRecord> Query(DateTimeOffset fromUtc, DateTimeOffset toUtc,
        string search = "", string kind = "", int limit = 100000)
    {
        if (toUtc <= fromUtc || limit <= 0) return [];
        lock (gate)
        {
            using var command = CreateRangeCommand(fromUtc, toUtc, search, kind);
            command.CommandText = "SELECT id,start_utc,end_utc,kind,process_name,title,url,domain FROM activity WHERE " +
                command.CommandText + " ORDER BY start_utc DESC, id DESC LIMIT $limit";
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();
            var records = new List<ActivityRecord>();
            while (reader.Read())
            {
                var item = ReadRecord(reader);
                records.Add(item with
                {
                    StartUtc = item.StartUtc < fromUtc ? fromUtc : item.StartUtc,
                    EndUtc = item.EndUtc > toUtc ? toUtc : item.EndUtc
                });
            }
            return records;
        }
    }

    public IReadOnlyList<ActivitySummary> Summarize(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        if (toUtc <= fromUtc) return [];
        lock (gate)
        {
            using var command = CreateRangeCommand(fromUtc, toUtc, "", "");
            command.CommandText = """
                SELECT CASE WHEN kind='website' THEN domain WHEN kind='idle' THEN 'Idle' ELSE process_name END AS label,
                    kind, SUM(MAX(0, MIN(end_utc,$to)-MAX(start_utc,$from)))/1000.0 AS seconds, COUNT(*) AS sessions
                FROM activity WHERE 
                """ + command.CommandText + " GROUP BY label,kind ORDER BY seconds DESC,label";
            using var reader = command.ExecuteReader();
            var summaries = new List<ActivitySummary>();
            while (reader.Read()) summaries.Add(new(reader.GetString(0), reader.GetString(1), reader.GetDouble(2), reader.GetInt32(3)));
            return summaries;
        }
    }

    private SqliteCommand CreateRangeCommand(DateTimeOffset fromUtc, DateTimeOffset toUtc, string search, string kind)
    {
        var command = connection.CreateCommand();
        // Include zero-duration observations in the range: seeing an app briefly is still an event.
        command.CommandText = "start_utc < $to AND (end_utc > $from OR (end_utc=start_utc AND start_utc >= $from))";
        command.Parameters.AddWithValue("$from", fromUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$to", toUtc.ToUnixTimeMilliseconds());
        if (!string.IsNullOrWhiteSpace(kind))
        {
            command.CommandText += " AND kind=$kind";
            command.Parameters.AddWithValue("$kind", kind);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            command.CommandText += " AND (process_name LIKE $search ESCAPE '\\' OR title LIKE $search ESCAPE '\\' OR url LIKE $search ESCAPE '\\' OR domain LIKE $search ESCAPE '\\')";
            command.Parameters.AddWithValue("$search", "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
        }
        return command;
    }

    public int DeleteBefore(DateTimeOffset cutoff)
    {
        lock (gate)
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM activity WHERE end_utc <= $cutoff; UPDATE activity SET start_utc=$cutoff WHERE start_utc < $cutoff;";
            command.Parameters.AddWithValue("$cutoff", cutoff.ToUnixTimeMilliseconds());
            var result = command.ExecuteNonQuery();
            transaction.Commit();
            return result;
        }
    }

    /// <summary>Removes only the requested interval, preserving portions on either side.</summary>
    public int DeleteRange(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        if (toUtc <= fromUtc) return 0;
        lock (gate)
        {
            var originals = new List<ActivityRecord>();
            using (var read = CreateRangeCommand(fromUtc, toUtc, "", ""))
            {
                read.CommandText = "SELECT id,start_utc,end_utc,kind,process_name,title,url,domain FROM activity WHERE " + read.CommandText;
                using var reader = read.ExecuteReader();
                while (reader.Read()) originals.Add(ReadRecord(reader));
            }
            using var transaction = connection.BeginTransaction();
            foreach (var item in originals)
            {
                using var delete = connection.CreateCommand();
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM activity WHERE id=$id";
                delete.Parameters.AddWithValue("$id", item.Id);
                delete.ExecuteNonQuery();
                if (item.StartUtc < fromUtc) UpsertInternal(item with { EndUtc = fromUtc }, transaction);
                if (item.EndUtc > toUtc) UpsertInternal(item with { Id = Guid.NewGuid().ToString("N"), StartUtc = toUtc }, transaction);
            }
            transaction.Commit();
            return originals.Count;
        }
    }

    public void DeleteAll()
    {
        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM activity; PRAGMA wal_checkpoint(TRUNCATE); VACUUM;";
            command.ExecuteNonQuery();
        }
    }

    public static void ExportCsv(IEnumerable<ActivityRecord> records, string path)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("Id,Start UTC,End UTC,Start local,End local,Duration seconds,Kind,Application,Window title,URL,Domain");
        foreach (var item in records)
            writer.WriteLine(string.Join(",", new[] {
                item.Id, item.StartUtc.ToUniversalTime().ToString("O"), item.EndUtc.ToUniversalTime().ToString("O"),
                item.StartUtc.ToLocalTime().ToString("O"), item.EndUtc.ToLocalTime().ToString("O"),
                item.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture), item.Kind, item.ProcessName,
                item.Title, item.Url, item.Domain
            }.Select(CsvCell)));
    }

    private static string CsvCell(string text)
    {
        // Treat untrusted window titles and URLs as data when opened in a spreadsheet.
        var trimmed = text.TrimStart();
        if (trimmed.Length > 0 && "=+-@\t\r\n".Contains(trimmed[0])) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    private static ActivityRecord ReadRecord(SqliteDataReader reader) => new(
        reader.GetString(0), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
        DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2)), reader.GetString(3),
        reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7));

    public void Dispose()
    {
        lock (gate) connection.Dispose();
    }
}
