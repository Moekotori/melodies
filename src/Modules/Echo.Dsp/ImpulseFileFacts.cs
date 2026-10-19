using System.Buffers.Binary;
using System.Text;

namespace Echo.Dsp;

internal readonly record struct ImpulseFileFacts(int Rate, int Channels, int Frames)
{
    public static ImpulseFileFacts Read(string path)
    {
        using var file = File.OpenRead(path); using var reader = new BinaryReader(file, Encoding.ASCII, leaveOpen: true);
        var magic = Encoding.ASCII.GetString(reader.ReadBytes(4)); ImpulseFileFacts facts;
        if (magic == "fLaC")
        {
            var header = reader.ReadBytes(4); if (header.Length != 4 || (header[0] & 127) != 0 || header[1] != 0 || header[2] != 0 || header[3] != 34) throw new IOException("FLAC 缺少 STREAMINFO。");
            var data = reader.ReadBytes(34); if (data.Length != 34) throw new IOException("FLAC 头部不完整。");
            var packed = BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(10));
            facts = new((int)(packed >> 44), (int)((packed >> 41) & 7) + 1, checked((int)(packed & 0xFFFFFFFFF)));
        }
        else if (magic is "RIFF" or "FORM")
        {
            var big = magic == "FORM"; reader.ReadUInt32(); var type = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (type is not ("WAVE" or "AIFF" or "AIFC")) throw new IOException("不支持此脉冲文件头。");
            int rate = 0, channels = 0, block = 0, frames = 0;
            for (var chunks = 0; chunks < 128 && file.Position + 8 <= file.Length; chunks++)
            {
                var name = Encoding.ASCII.GetString(reader.ReadBytes(4)); var raw = reader.ReadBytes(4);
                var length = big ? BinaryPrimitives.ReadUInt32BigEndian(raw) : BinaryPrimitives.ReadUInt32LittleEndian(raw);
                var end = checked(file.Position + length + (length & 1)); if (end > file.Length + 1) throw new IOException("脉冲文件数据块无效。");
                if (name == "fmt " && length >= 16)
                { reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32(); reader.ReadUInt32(); block = reader.ReadUInt16(); }
                else if (name == "data" && block > 0) frames = checked((int)(length / block));
                else if (name == "COMM" && length >= 18)
                {
                    var data = reader.ReadBytes(18); channels = BinaryPrimitives.ReadUInt16BigEndian(data); frames = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(2)));
                    var exponent = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(8)); var mantissa = BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(10));
                    rate = checked((int)Math.Round(Math.ScaleB(mantissa, (exponent & 0x7FFF) - 16383 - 63)));
                }
                file.Position = Math.Min(end, file.Length);
            }
            facts = new(rate, channels, frames);
        }
        else throw new IOException("无法识别脉冲响应文件。");
        if (facts.Rate is < 8000 or > 768000 || facts.Channels is < 1 or > 32 || facts.Frames is < 1 or > 131072 || (long)facts.Channels * facts.Frames > 262144)
            throw new IOException("脉冲的采样率、声道或长度超出原生卷积预算。");
        return facts;
    }
}
