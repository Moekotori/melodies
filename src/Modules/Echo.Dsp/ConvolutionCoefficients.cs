using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Echo.Dsp;

internal sealed record ConvolutionCoefficients(int Rate, int Channels, int Frames, double[] Samples)
{
    public static byte[] ReadBytes(ZipArchiveEntry entry, int maximum)
    {
        if (entry.Length <= 0 || entry.Length > maximum) throw new IOException("卷积包条目超过预算。");
        using var source = entry.Open(); using var output = new MemoryStream((int)entry.Length); var buffer = new byte[16384]; int count;
        while ((count = source.Read(buffer)) > 0)
        { if (output.Length + count > entry.Length || output.Length + count > maximum) throw new IOException("卷积包解压超过预算。"); output.Write(buffer, 0, count); }
        if (output.Length != entry.Length) throw new IOException("卷积包条目不完整。");
        return output.ToArray();
    }
    public static ConvolutionCoefficients Read(ZipArchiveEntry entry, int configuredRate)
    {
        if (entry.Length is <= 0 or > 8 * 1024 * 1024) throw new IOException("卷积系数文件超过预算。");
        var bytes = ReadBytes(entry, 8 * 1024 * 1024);
        if (bytes.LongLength != entry.Length) throw new IOException("卷积系数文件不完整。");
        var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
        var rate = configuredRate; var channels = 1; var bits = extension == ".pcm" ? 32 : 64; var format = 3;
        var offset = 0; var length = bytes.Length;
        if (extension is not (".pcm" or ".dbl"))
        {
            if (extension != ".wav" || bytes.Length < 44 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
                throw new IOException("路由配置的系数请使用 WAV、PCM Float32 或 DBL Float64；压缩系数请先导出为 WAV。");
            var found = false;
            for (var chunk = 12; chunk + 8 <= bytes.Length;)
            {
                var size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(chunk + 4)); var start = chunk + 8;
                if (size < 0 || size > bytes.Length - start) throw new IOException("WAV 系数块无效。");
                var name = Encoding.ASCII.GetString(bytes, chunk, 4);
                if (name == "fmt " && size >= 16)
                { format = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(start)); channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(start + 2)); rate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(start + 4)); bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(start + 14)); }
                if (name == "data") { offset = start; length = size; found = true; }
                chunk = checked(start + size + (size & 1));
            }
            if (!found) throw new IOException("WAV 缺少系数数据。");
        }
        if (rate != configuredRate || channels is < 1 or > 32 || bits is not (16 or 24 or 32 or 64) || format is not (1 or 3) || format == 3 && bits is not (32 or 64) || format == 1 && bits == 64) throw new IOException("卷积系数格式不受支持。");
        var width = bits / 8; var frames = length / (width * channels);
        if (length % (width * channels) != 0 || frames is < 1 or > 131072 || frames * channels > 262144) throw new IOException("卷积系数超过原生预算。");
        var samples = new double[frames * channels];
        for (var i = 0; i < samples.Length; i++)
        {
            var span = bytes.AsSpan(offset + i * width, width);
            var value = format == 3 ? bits == 32 ? BinaryPrimitives.ReadSingleLittleEndian(span) : BinaryPrimitives.ReadDoubleLittleEndian(span)
                : bits switch { 16 => BinaryPrimitives.ReadInt16LittleEndian(span) / 32768d, 24 => ((span[0] | span[1] << 8 | span[2] << 16) << 8 >> 8) / 8388608d, _ => BinaryPrimitives.ReadInt32LittleEndian(span) / 2147483648d };
            if (!double.IsFinite(value)) throw new IOException("卷积系数包含非有限数值。"); samples[i] = value;
        }
        return new(rate, channels, frames, samples);
    }
}
