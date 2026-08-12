namespace NetPack.Config;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// One entry in a preset's <c>hooks</c> array. It is either a bare string (a
/// module reference, shorthand for <c>{ "source": "…" }</c>) or an object that
/// can also constrain and parameterize the hook:
/// <list type="bullet">
///   <item><b>source</b> — the hook module (path or package reference). Required.</item>
///   <item><b>test</b> — a regular expression matched against the file/module name;
///   the hook only runs for matching ones (default: match everything).</item>
///   <item><b>exclude</b> — a regular expression for names to <i>skip</i> (applied
///   after <c>test</c>).</item>
///   <item><b>mode</b> — <c>dev</c>, <c>prod</c> or <c>both</c> (default): run only
///   in the dev server, only in optimized builds, or always.</item>
///   <item><b>order</b> — an integer that shifts the hook earlier (negative) or
///   later (positive) among the hooks for the same phase (default 0).</item>
///   <item><b>name</b> — a label surfaced in diagnostics and passed to the hook.</item>
///   <item><b>options</b> — an arbitrary JSON value handed to the hook function as
///   <c>payload.options</c> (default: <c>{}</c>).</item>
/// </list>
/// </summary>
[JsonConverter(typeof(HookEntryConverter))]
public sealed class HookEntry
{
    /// <summary>The hook module reference (path or package).</summary>
    public string Source { get; set; } = "";

    /// <summary>Regex source matched against the file/module name, or null for all.</summary>
    public string? Test { get; set; }

    /// <summary>Regex source for names to skip (applied after <see cref="Test"/>), or null.</summary>
    public string? Exclude { get; set; }

    /// <summary><c>dev</c>/<c>prod</c>/<c>both</c> — which builds the hook runs in, or null (both).</summary>
    public string? Mode { get; set; }

    /// <summary>Ordering nudge within the phase; lower runs earlier (default 0).</summary>
    public int Order { get; set; }

    /// <summary>A label for diagnostics, also passed to the hook, or null.</summary>
    public string? Name { get; set; }

    /// <summary>Caller-supplied options passed through to the hook, or null.</summary>
    public JsonElement? Options { get; set; }
}

/// <summary>Reads a <see cref="HookEntry"/> from either a string (shorthand for a
/// source-only entry) or a full object. Hand-written so it stays AoT-safe.</summary>
internal sealed class HookEntryConverter : JsonConverter<HookEntry>
{
    public override HookEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new HookEntry { Source = reader.GetString() ?? "" };
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var element = document.RootElement;
            var entry = new HookEntry();

            if (element.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.String)
            {
                entry.Source = source.GetString() ?? "";
            }

            if (element.TryGetProperty("test", out var test) && test.ValueKind == JsonValueKind.String)
            {
                entry.Test = test.GetString();
            }

            if (element.TryGetProperty("exclude", out var exclude) && exclude.ValueKind == JsonValueKind.String)
            {
                entry.Exclude = exclude.GetString();
            }

            if (element.TryGetProperty("mode", out var mode) && mode.ValueKind == JsonValueKind.String)
            {
                entry.Mode = mode.GetString();
            }

            if (element.TryGetProperty("order", out var order) && order.ValueKind == JsonValueKind.Number && order.TryGetInt32(out var value))
            {
                entry.Order = value;
            }

            if (element.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            {
                entry.Name = name.GetString();
            }

            if (element.TryGetProperty("options", out var opts) && opts.ValueKind != JsonValueKind.Null)
            {
                entry.Options = opts.Clone();
            }

            return entry;
        }

        throw new JsonException("A hook entry must be a string or an object with a \"source\".");
    }

    public override void Write(Utf8JsonWriter writer, HookEntry value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("source", value.Source);

        if (value.Test is not null)
        {
            writer.WriteString("test", value.Test);
        }

        if (value.Exclude is not null)
        {
            writer.WriteString("exclude", value.Exclude);
        }

        if (value.Mode is not null)
        {
            writer.WriteString("mode", value.Mode);
        }

        if (value.Order != 0)
        {
            writer.WriteNumber("order", value.Order);
        }

        if (value.Name is not null)
        {
            writer.WriteString("name", value.Name);
        }

        if (value.Options is { } opts)
        {
            writer.WritePropertyName("options");
            opts.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
}
