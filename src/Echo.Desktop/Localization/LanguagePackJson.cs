using System.Text.Encodings.Web;
using System.Text.Json;

namespace Echo.Desktop.Localization;

internal static class LanguagePackJson
{
    public static byte[] Serialize(LanguagePack pack)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject(); writer.WriteString("code", pack.Code); writer.WriteString("nativeName", pack.NativeName);
            writer.WriteStartObject("strings");
            foreach (var pair in pack.Strings.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WriteString(pair.Key, pair.Value); writer.Flush();
                if (buffer.Length > 256 * 1024) throw new InvalidDataException("Language pack exceeds 256 KiB.");
            }
            writer.WriteEndObject(); writer.WriteEndObject();
        }
        if (buffer.Length > 256 * 1024) throw new InvalidDataException("Language pack exceeds 256 KiB.");
        return buffer.ToArray();
    }
}
