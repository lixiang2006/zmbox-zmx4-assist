using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZmboxZmx4Assist.Domain;

/// <summary>
/// Persists recorded events either as compact positional arrays (current schema) or as the older
/// object form. A macro is mostly mouse moves, so the property names alone used to be ~60% of the
/// file: <c>[[offset, kind, virtualKey, button, x, y, wheel], ...]</c> keeps the same data without
/// the repetition, while every file written before schema v2 still loads unchanged.
/// </summary>
public sealed class RecordedEventsJsonConverter : JsonConverter<IReadOnlyList<RecordedEvent>>
{
    public override IReadOnlyList<RecordedEvent> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("宏事件必须是数组。");
        var events = new List<RecordedEvent>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.EndArray:
                    return events;
                case JsonTokenType.StartObject:
                    events.Add(ReadObject(ref reader));
                    break;
                case JsonTokenType.StartArray:
                    events.Add(ReadCompact(ref reader));
                    break;
                default:
                    throw new JsonException("宏事件的每一项必须是对象或数组。");
            }
        }
        throw new JsonException("宏事件数组没有正常结束。");
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<RecordedEvent> value, JsonSerializerOptions options)
    {
        // The rest of the macro stays human-readable; the event block is written compactly in one
        // raw write so indentation cannot re-introduce the whitespace this format exists to remove.
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var compact = new Utf8JsonWriter(buffer))
        {
            compact.WriteStartArray();
            foreach (var e in value)
            {
                compact.WriteStartArray();
                compact.WriteNumberValue(e.OffsetMicroseconds);
                compact.WriteNumberValue((int)e.Kind);
                compact.WriteNumberValue(e.VirtualKey);
                compact.WriteNumberValue((int)e.Button);
                compact.WriteNumberValue(e.X);
                compact.WriteNumberValue(e.Y);
                compact.WriteNumberValue(e.WheelDelta);
                compact.WriteEndArray();
            }
            compact.WriteEndArray();
        }
        writer.WriteRawValue(buffer.WrittenSpan, skipInputValidation: true);
    }

    private static RecordedEvent ReadCompact(ref Utf8JsonReader reader)
    {
        var values = new long[7];
        var index = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.Number) throw new JsonException("紧凑宏事件的字段必须是数字。");
            if (index >= values.Length) throw new JsonException("紧凑宏事件最多包含 7 个字段。");
            values[index++] = reader.GetInt64();
        }
        if (index < 2) throw new JsonException("紧凑宏事件至少需要偏移和类型两个字段。");
        return new RecordedEvent
        {
            OffsetMicroseconds = values[0],
            Kind = (InputEventKind)values[1],
            VirtualKey = (int)values[2],
            Button = (MouseButtonKind)values[3],
            X = (int)values[4],
            Y = (int)values[5],
            WheelDelta = (int)values[6]
        };
    }

    private static RecordedEvent ReadObject(ref Utf8JsonReader reader)
    {
        var result = new RecordedEvent();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("宏事件对象的属性名无效。");
            var name = reader.GetString();
            reader.Read();
            switch (name?.ToUpperInvariant())
            {
                case "OFFSETMICROSECONDS":
                    result = result with { OffsetMicroseconds = reader.GetInt64() };
                    break;
                case "KIND":
                    result = result with { Kind = ReadKind(ref reader) };
                    break;
                case "VIRTUALKEY":
                    result = result with { VirtualKey = reader.GetInt32() };
                    break;
                case "BUTTON":
                    result = result with { Button = ReadButton(ref reader) };
                    break;
                case "X":
                    result = result with { X = reader.GetInt32() };
                    break;
                case "Y":
                    result = result with { Y = reader.GetInt32() };
                    break;
                case "WHEELDELTA":
                    result = result with { WheelDelta = reader.GetInt32() };
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        return result;
    }

    private static InputEventKind ReadKind(ref Utf8JsonReader reader) => reader.TokenType == JsonTokenType.String
        ? Enum.TryParse<InputEventKind>(reader.GetString(), ignoreCase: true, out var kind)
            ? kind
            : throw new JsonException("宏事件类型名称无法识别。")
        : (InputEventKind)reader.GetInt32();

    private static MouseButtonKind ReadButton(ref Utf8JsonReader reader) => reader.TokenType == JsonTokenType.String
        ? Enum.TryParse<MouseButtonKind>(reader.GetString(), ignoreCase: true, out var button)
            ? button
            : throw new JsonException("宏事件鼠标按键名称无法识别。")
        : (MouseButtonKind)reader.GetInt32();
}
