using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    public Task<IReadOnlyList<ScanIssue>> GetScanIssuesAsync(string jobId, int limit = 16,
        CancellationToken cancellation = default)
    {
        if (jobId.Length > 64 || limit is < 1 or > 64) throw new ArgumentException("Invalid issue query.");
        return _interactiveReader.QueueAsync<IReadOnlyList<ScanIssue>>(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT ordinal,path,code FROM scan_errors WHERE scan_id=$id ORDER BY ordinal LIMIT $limit";
            command.Parameters.AddWithValue("$id", jobId);
            command.Parameters.AddWithValue("$limit", limit);
            var issues = new List<ScanIssue>(limit);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                issues.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
            }
            return issues;
        }, true, cancellation);
    }
}
