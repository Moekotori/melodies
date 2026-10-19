using System.Buffers.Binary;
using Echo.Core.Protocol;

namespace Echo.Core.Media;

/// <summary>RFC 9639 sample offsets, independent of text syntax and physical audio decoding.</summary>
internal static class FlacBinaryCueSheet
{
    public static (int Rate, ulong Samples) StreamInfo(byte[] bytes)
    {
        if (bytes.Length != 34) throw new IOException("Invalid FLAC STREAMINFO.");
        var packed = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(10, 8));
        return ((int)(packed >> 44), packed & 0xfffffffffUL);
    }

    public static List<ScanCueTrackDetails> Parse(byte[] bytes, string path, int rate, ulong totalSamples, CancellationToken cancellation)
    {
        void Fail() => throw new IOException("Invalid FLAC CUESHEET.");
        if (bytes.Length < 396 || rate <= 0) Fail();
        var cd = (bytes[136] & 128) != 0;
        var count = bytes[395];
        if (count == 0 || cd && (count > 100 || rate != 44100)) Fail();
        var tracks = new List<ScanCueTrackDetails>();
        var numbers = new HashSet<byte>();
        var offset = 396;
        ulong lastOffset = 0;
        ulong Samples(int position) {
            if (position + 8 > bytes.Length) Fail();
            var value = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(position, 8));
            if (value > 9007199254740991UL || totalSamples > 0 && value > totalSamples) Fail();
            return value;
        }
        void Bound(double boundary) {
            if (tracks.LastOrDefault() is not { } previous) return;
            if (boundary <= previous.StartSeconds) Fail();
            if (previous.EndSeconds is null) tracks[^1] = previous with { EndSeconds = boundary };
        }
        for (var trackIndex = 0; trackIndex < count; trackIndex++) {
            cancellation.ThrowIfCancellationRequested();
            if (offset + 36 > bytes.Length) Fail();
            var trackOffset = Samples(offset);
            var number = bytes[offset + 8]; var indices = bytes[offset + 35];
            var data = (bytes[offset + 21] & 128) != 0;
            if (number == 0 || !numbers.Add(number) || trackOffset < lastOffset || cd && trackOffset % 588 != 0) Fail();
            lastOffset = trackOffset; offset += 36;
            if (trackIndex == count - 1) {
                if (number != (cd ? 170 : 255) || indices != 0) Fail();
                Bound((double)trackOffset / rate); continue;
            }
            if (number == (cd ? 170 : 255) || indices == 0 || cd && (number > 99 || indices > 100)) Fail();
            double? start = null, pregap = null;
            var lastIndex = -1; ulong lastRelative = 0;
            for (var index = 0; index < indices; index++) {
                cancellation.ThrowIfCancellationRequested();
                if (offset + 12 > bytes.Length) Fail();
                var relative = Samples(offset); var indexNumber = bytes[offset + 8];
                var absolute = trackOffset + relative;
                if (index == 0 && indexNumber > 1 || index > 0 && indexNumber != lastIndex + 1 || relative < lastRelative
                    || cd && relative % 588 != 0 || absolute > 9007199254740991UL || totalSamples > 0 && absolute > totalSamples) Fail();
                var seconds = (double)absolute / rate;
                if (indexNumber == 0) pregap = seconds;
                if (indexNumber == 1) start = seconds;
                lastIndex = indexNumber; lastRelative = relative; offset += 12;
            }
            if (data) { Bound((double)trackOffset / rate); continue; }
            if (start is null || tracks.LastOrDefault() is { } last && start <= last.StartSeconds) Fail();
            Bound(start!.Value);
            tracks.Add(new(path, path, "embedded", number, null, null, null, null, pregap, start.Value, null));
        }
        if (offset != bytes.Length) Fail();
        return tracks;
    }
}
