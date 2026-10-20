namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    public Task<(string Name, string Description)> GetPlaylistMetadataAsync(string id, CancellationToken cancellation)
    {
        ValidatePlaylistId(id);
        return _interactiveReader.QueueAsync(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name,description FROM playlists WHERE id=$id AND archived=0";
            command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            return reader.Read() ? (reader.GetString(0), reader.GetString(1))
                : throw new InvalidOperationException("歌单已删除或不存在，请刷新后重试。");
        }, false, cancellation);
    }
}
