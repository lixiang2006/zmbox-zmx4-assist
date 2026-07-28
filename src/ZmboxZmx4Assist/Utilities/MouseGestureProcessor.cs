using ZmboxZmx4Assist.Domain;

namespace ZmboxZmx4Assist.Utilities;

public static class MouseGestureProcessor
{
    public static IReadOnlyList<RecordedEvent> Normalize(IReadOnlyList<RecordedEvent> source, int clickThresholdPixels = 5, int clickThresholdMilliseconds = 300)
    {
        var result = new List<RecordedEvent>();
        var pending = new Dictionary<MouseButtonKind, List<RecordedEvent>>();

        foreach (var item in source)
        {
            if (item.Kind == InputEventKind.MouseDown && item.Button != MouseButtonKind.None)
            {
                pending[item.Button] = new List<RecordedEvent> { item };
                continue;
            }

            if (item.Kind == InputEventKind.MouseUp && item.Button != MouseButtonKind.None && pending.Remove(item.Button, out var gesture))
            {
                gesture.Add(item);
                var down = gesture[0];
                var deltaMs = (item.OffsetMicroseconds - down.OffsetMicroseconds) / 1000.0;
                var maxDistance = gesture.Where(x => x.Kind == InputEventKind.MouseMove)
                    .Select(x => Math.Max(Math.Abs(x.X - down.X), Math.Abs(x.Y - down.Y))).DefaultIfEmpty(0).Max();
                if (deltaMs <= clickThresholdMilliseconds && maxDistance <= clickThresholdPixels)
                {
                    result.Add(down);
                    result.Add(item);
                }
                else result.AddRange(gesture);
                continue;
            }

            var active = pending.Values.ToList();
            if (item.Kind == InputEventKind.MouseMove && active.Count > 0)
                active.ForEach(x => x.Add(item));
            else result.Add(item);
        }

        foreach (var unclosed in pending.Values) result.AddRange(unclosed);
        return result.OrderBy(x => x.OffsetMicroseconds).ToArray();
    }
}
