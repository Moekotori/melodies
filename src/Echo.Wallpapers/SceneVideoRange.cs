using System.Buffers.Binary;
using System.Text;

namespace Echo.Wallpapers;

internal static class SceneVideoRange
{
    public static async Task<SceneFile?> ReadAsync(SceneFile entry, CancellationToken token)
    {
        if (entry.Length < 96) return null;
        await using var file = File.OpenRead(entry.Path); file.Position = entry.Offset;
        var head = new byte[96]; await file.ReadExactlyAsync(head, token).ConfigureAwait(false);
        int Number(int offset) => BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(offset));
        if (Encoding.ASCII.GetString(head, 0, 8) != "TEXV0005" || (Number(22) & 32) == 0) return null;
        var version = head[53] - (byte)'0';
        if (Encoding.ASCII.GetString(head, 46, 7) != "TEXB000" || version is < 1 or > 9) return null;
        var position = 59 + (version >= 3 ? 4 : 0) + (version >= 4 ? 4 : 0);
        var mipCount = Number(position); position += 4;
        if (mipCount is < 1 or > 32) return null;
        var bytes = version >= 2 ? 20 : 12;
        var length = Number(position + bytes - 4);
        var start = position + bytes;
        if (length <= 0 || start + (long)length > entry.Length) throw new InvalidDataException("视频纹理范围无效");
        return new(entry.Path, entry.Offset + start, length);
    }
}
