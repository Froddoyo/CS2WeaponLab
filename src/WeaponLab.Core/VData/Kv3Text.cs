using System.Globalization;
using System.Text;
using ValveKeyValue;

namespace WeaponLab.Core.VData;

public enum Kv3NodeKind
{
    Object,
    Array,
    Scalar,
}

/// <summary>A value in a KV3 text file, with where its text sits so it can be replaced in place.</summary>
/// <param name="Start">First character of the value.</param>
/// <param name="End">One past the last character of the value.</param>
public sealed class Kv3Node(Kv3NodeKind kind, int start)
{
    public Kv3NodeKind Kind { get; } = kind;
    public int Start { get; } = start;
    public int End { get; set; }

    /// <summary>Where the "key =" of this value starts, for removing the whole entry (-1 for array items and the root).</summary>
    public int KeyStart { get; set; } = -1;

    /// <summary>Object members in file order.</summary>
    public List<(string Key, Kv3Node Value)> Members { get; } = [];

    public List<Kv3Node> Items { get; } = [];

    public Kv3Node? Member(string key) => Members.FirstOrDefault(member => member.Key == key).Value;
}

/// <summary>
/// Reads KV3 text (Valve's .vdata source format) into nodes that remember
/// their position, and writes values back in the same style. Comments and
/// formatting outside the edited values are never touched.
/// </summary>
public static class Kv3Text
{
    public static Kv3Node Parse(string text)
    {
        var position = 0;
        SkipSpace(text, ref position);
        if (text.AsSpan(position).StartsWith("<!--", StringComparison.Ordinal))
        {
            var close = text.IndexOf("-->", position, StringComparison.Ordinal);
            position = close < 0 ? throw Error(text, position, "unclosed header") : close + 3;
        }

        SkipSpace(text, ref position);
        var root = ParseValue(text, ref position);
        return root.Kind == Kv3NodeKind.Object ? root : throw Error(text, position, "the file does not start with an object");
    }

    private static Kv3Node ParseValue(string text, ref int position)
    {
        if (position >= text.Length)
        {
            throw Error(text, position, "unexpected end of file");
        }

        var start = position;
        var character = text[position];
        if (character == '{')
        {
            var node = new Kv3Node(Kv3NodeKind.Object, start);
            position++;
            while (true)
            {
                SkipSpace(text, ref position);
                if (position < text.Length && text[position] == '}')
                {
                    position++;
                    break;
                }

                var keyStart = position;
                var key = text[position] == '"' ? Unquote(ReadString(text, ref position)) : ReadToken(text, ref position);
                SkipSpace(text, ref position);
                if (position >= text.Length || text[position] != '=')
                {
                    throw Error(text, position, $"expected '=' after {key}");
                }

                position++;
                SkipSpace(text, ref position);
                var value = ParseValue(text, ref position);
                value.KeyStart = keyStart;
                node.Members.Add((key, value));
                SkipSpace(text, ref position);
                if (position < text.Length && text[position] == ',')
                {
                    position++;
                }
            }

            node.End = position;
            return node;
        }

        if (character == '[')
        {
            var node = new Kv3Node(Kv3NodeKind.Array, start);
            position++;
            while (true)
            {
                SkipSpace(text, ref position);
                if (position < text.Length && text[position] == ']')
                {
                    position++;
                    break;
                }

                node.Items.Add(ParseValue(text, ref position));
                SkipSpace(text, ref position);
                if (position < text.Length && text[position] == ',')
                {
                    position++;
                }
            }

            node.End = position;
            return node;
        }

        var scalar = new Kv3Node(Kv3NodeKind.Scalar, start);
        if (character == '"')
        {
            ReadString(text, ref position);
        }
        else if (character == '#' && position + 1 < text.Length && text[position + 1] == '[')
        {
            var close = text.IndexOf(']', position);
            position = close < 0 ? throw Error(text, position, "unclosed blob") : close + 1;
        }
        else
        {
            ReadToken(text, ref position);
            // A flagged string: resource_name:"...", soundevent:"..."
            if (position < text.Length && text[position] == ':' && position + 1 < text.Length && text[position + 1] == '"')
            {
                position++;
                ReadString(text, ref position);
            }
        }

        scalar.End = position;
        return scalar;
    }

    private static string ReadString(string text, ref int position)
    {
        var start = position;
        if (text.AsSpan(position).StartsWith("\"\"\"", StringComparison.Ordinal))
        {
            var close = text.IndexOf("\"\"\"", position + 3, StringComparison.Ordinal);
            position = close < 0 ? throw Error(text, position, "unclosed multi-line string") : close + 3;
            return text[start..position];
        }

        position++;
        while (position < text.Length && text[position] != '"')
        {
            position += text[position] == '\\' ? 2 : 1;
        }

        if (position >= text.Length)
        {
            throw Error(text, start, "unclosed string");
        }

        position++;
        return text[start..position];
    }

    private static string ReadToken(string text, ref int position)
    {
        var start = position;
        while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not (',' or ']' or '}' or '=' or ':'))
        {
            position++;
        }

        return position > start ? text[start..position] : throw Error(text, start, "expected a value");
    }

    private static void SkipSpace(string text, ref int position)
    {
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                position++;
            }
            else if (text.AsSpan(position).StartsWith("//", StringComparison.Ordinal))
            {
                var end = text.IndexOf('\n', position);
                position = end < 0 ? text.Length : end + 1;
            }
            else if (text.AsSpan(position).StartsWith("/*", StringComparison.Ordinal))
            {
                var end = text.IndexOf("*/", position + 2, StringComparison.Ordinal);
                position = end < 0 ? text.Length : end + 2;
            }
            else
            {
                return;
            }
        }
    }

    private static InvalidDataException Error(string text, int position, string message)
    {
        var line = text.AsSpan(0, Math.Min(position, text.Length)).Count('\n') + 1;
        return new InvalidDataException($"weapons.vdata line {line}: {message}.");
    }

    /// <summary>The text of a scalar without quotes or a flag prefix: <c>resource_name:"a.vmdl"</c> → <c>a.vmdl</c>.</summary>
    public static string ScalarValue(string text, Kv3Node node)
    {
        var raw = text[node.Start..node.End];
        var quote = raw.IndexOf('"');
        return quote < 0 ? raw : Unquote(raw[quote..]);
    }

    private static string Unquote(string quoted) =>
        quoted.StartsWith("\"\"\"", StringComparison.Ordinal) ? quoted[3..^3]
        : quoted[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);

    /// <summary>
    /// A scalar written like the one it replaces: same flag prefix and
    /// quoting, and floats keep a decimal point so the compiler still reads
    /// them as floats (1 → 1.0).
    /// </summary>
    public static string FormatLike(string existing, string value, KVValueType type)
    {
        var quote = existing.IndexOf('"');
        if (quote >= 0)
        {
            return existing[..quote] + Quote(value);
        }

        return type switch
        {
            KVValueType.FloatingPoint or KVValueType.FloatingPoint64 => Float(value),
            KVValueType.String => Quote(value),
            _ => existing.Contains('.') && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? Float(value) : value,
        };
    }

    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string Float(string value) =>
        value.Contains('.') || value.Contains('e') || value.Contains('E') ? value : value + ".0";

    /// <summary>Writes a compiled value as KV3 text, one level deeper than <paramref name="indent"/>.</summary>
    public static string Write(KVObject value, int indent)
    {
        var builder = new StringBuilder();
        Write(builder, value, indent);
        return builder.ToString();
    }

    private static void Write(StringBuilder builder, KVObject value, int indent)
    {
        if (value.IsArray)
        {
            var items = value.Values.ToList();
            if (items.All(item => !item.IsArray && !item.IsCollection))
            {
                builder.Append("[ ").AppendJoin(", ", items.Select(item => Write(item, indent))).Append(" ]");
                return;
            }

            builder.Append('[').Append('\n');
            foreach (var item in items)
            {
                builder.Append('\t', indent + 1);
                Write(builder, item, indent + 1);
                builder.Append(",\n");
            }

            builder.Append('\t', indent).Append(']');
            return;
        }

        if (value.IsCollection)
        {
            builder.Append("{\n");
            foreach (var (key, child) in value.Children)
            {
                builder.Append('\t', indent + 1).Append(key).Append(" = ");
                Write(builder, child, indent + 1);
                builder.Append('\n');
            }

            builder.Append('\t', indent).Append('}');
            return;
        }

        builder.Append(Scalar(value));
    }

    private static string Scalar(KVObject value)
    {
        var prefix = value.Flag switch
        {
            KVFlag.Resource => "resource:",
            KVFlag.ResourceName => "resource_name:",
            KVFlag.Panorama => "panorama:",
            KVFlag.SoundEvent => "soundevent:",
            KVFlag.SubClass => "subclass:",
            _ => "",
        };
        return value.ValueType switch
        {
            KVValueType.String => prefix + Quote(value.ToString() ?? ""),
            KVValueType.Boolean => value.ToBoolean(CultureInfo.InvariantCulture) ? "true" : "false",
            KVValueType.FloatingPoint or KVValueType.FloatingPoint64 => Float(value.ToDouble(CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture)),
            KVValueType.Null => "null",
            _ => value.ToString(CultureInfo.InvariantCulture) ?? "",
        };
    }
}
