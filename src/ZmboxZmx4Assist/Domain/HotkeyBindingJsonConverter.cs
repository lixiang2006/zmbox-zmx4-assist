using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZmboxZmx4Assist.Domain;

public sealed class HotkeyBindingJsonConverter : JsonConverter<HotkeyBinding>
{
    public override HotkeyBinding Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return new HotkeyBinding(reader.GetUInt32());

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
                virtualKey = reader.GetUInt32();
            else if (string.Equals(property, "Modifiers", StringComparison.OrdinalIgnoreCase))
                modifiers = reader.TokenType == JsonTokenType.String
                    ? Enum.Parse<HotkeyModifiers>(reader.GetString()!, ignoreCase: true)
                    : (HotkeyModifiers)reader.GetUInt32();
            else
                reader.Skip();
        }

        return new HotkeyBinding(virtualKey, modifiers);
    }

    public override void Write(Utf8JsonWriter writer, HotkeyBinding value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("VirtualKey", value.VirtualKey);
        writer.WriteNumber("Modifiers", (uint)value.Modifiers);
        writer.WriteEndObject();
    }
}
