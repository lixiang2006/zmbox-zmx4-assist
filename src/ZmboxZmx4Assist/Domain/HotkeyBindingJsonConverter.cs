using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZmboxZmx4Assist.Domain;

public sealed class HotkeyBindingJsonConverter : JsonConverter<HotkeyBinding>
{
    public override HotkeyBinding Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return new HotkeyBinding(ReadVirtualKey(ref reader, "热键"));

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("热键必须是旧版数字或组合热键对象。");

        uint virtualKey = 0;
        var modifiers = HotkeyModifiers.None;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("热键对象格式无效。");

            var property = reader.GetString();
            reader.Read();
            if (string.Equals(property, "VirtualKey", StringComparison.OrdinalIgnoreCase))
                virtualKey = ReadVirtualKey(ref reader, "热键的 VirtualKey");
            else if (string.Equals(property, "Modifiers", StringComparison.OrdinalIgnoreCase))
                modifiers = ReadModifiers(ref reader);
            else
                reader.Skip();
        }

        return new HotkeyBinding(virtualKey, modifiers);
    }

    // A corrupt or hand-edited settings.json must surface as JsonException (the documented
    // "bad data" signal handled by MacroLibraryService) instead of a raw InvalidOperationException.
    private static uint ReadVirtualKey(ref Utf8JsonReader reader, string field)
    {
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetUInt32(out var virtualKey))
            throw new JsonException($"{field} 必须是正整数。");
        return virtualKey;
    }

    private static HotkeyModifiers ReadModifiers(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return Enum.TryParse<HotkeyModifiers>(reader.GetString(), ignoreCase: true, out var modifiers)
                ? modifiers
                : throw new JsonException("热键的 Modifiers 名称无法识别。");
        }
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetUInt32(out var numeric))
            throw new JsonException("热键的 Modifiers 必须是数字或名称。");
        return (HotkeyModifiers)numeric;
    }

    public override void Write(Utf8JsonWriter writer, HotkeyBinding value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("VirtualKey", value.VirtualKey);
        writer.WriteNumber("Modifiers", (uint)value.Modifiers);
        writer.WriteEndObject();
    }
}
