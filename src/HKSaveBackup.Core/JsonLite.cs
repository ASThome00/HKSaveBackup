using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HKSaveBackup.Core
{
    /// <summary>
    /// Minimal flat-JSON reader/writer for the backup sidecar files.
    ///
    /// Deliberately hand-rolled: the mod runs against whatever Newtonsoft.Json build the
    /// game ships, while the test runner would need its own copy — a version-mismatch
    /// headache for a schema this trivial. Only flat objects with string/number/bool/null
    /// values are supported; nested containers are rejected.
    /// </summary>
    public static class JsonLite
    {
        public static string Write(IEnumerable<KeyValuePair<string, object>> values)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            bool first = true;
            foreach (var kv in values)
            {
                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("  ");
                WriteString(sb, kv.Key);
                sb.Append(": ");
                WriteValue(sb, kv.Value);
            }
            sb.Append("\n}");
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case float f:
                    sb.Append(((double)f).ToString("R", CultureInfo.InvariantCulture));
                    break;
                default:
                    throw new ArgumentException($"Unsupported JSON value type: {value.GetType()}");
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /// <summary>Parse a flat JSON object. Throws <see cref="FormatException"/> on malformed or nested input.</summary>
        public static Dictionary<string, object> Parse(string json)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            int pos = 0;
            SkipWhitespace(json, ref pos);
            Expect(json, ref pos, '{');
            SkipWhitespace(json, ref pos);
            if (Peek(json, pos) == '}')
            {
                pos++;
                return result;
            }
            while (true)
            {
                SkipWhitespace(json, ref pos);
                string key = ParseString(json, ref pos);
                SkipWhitespace(json, ref pos);
                Expect(json, ref pos, ':');
                SkipWhitespace(json, ref pos);
                result[key] = ParseValue(json, ref pos);
                SkipWhitespace(json, ref pos);
                char c = Next(json, ref pos);
                if (c == '}')
                    return result;
                if (c != ',')
                    throw new FormatException($"Expected ',' or '}}' at position {pos - 1}");
            }
        }

        private static object ParseValue(string json, ref int pos)
        {
            char c = Peek(json, pos);
            if (c == '"')
                return ParseString(json, ref pos);
            if (c == '{' || c == '[')
                throw new FormatException("Nested JSON containers are not supported");
            if (json.Length - pos >= 4 && json.Substring(pos, 4) == "true") { pos += 4; return true; }
            if (json.Length - pos >= 5 && json.Substring(pos, 5) == "false") { pos += 5; return false; }
            if (json.Length - pos >= 4 && json.Substring(pos, 4) == "null") { pos += 4; return null; }

            int start = pos;
            while (pos < json.Length && (char.IsDigit(json[pos]) || json[pos] == '-' || json[pos] == '+' ||
                                         json[pos] == '.' || json[pos] == 'e' || json[pos] == 'E'))
                pos++;
            if (pos == start)
                throw new FormatException($"Unexpected character '{c}' at position {pos}");
            string token = json.Substring(start, pos - start);
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                throw new FormatException($"Invalid number '{token}'");
            return number;
        }

        private static string ParseString(string json, ref int pos)
        {
            Expect(json, ref pos, '"');
            var sb = new StringBuilder();
            while (true)
            {
                char c = Next(json, ref pos);
                if (c == '"')
                    return sb.ToString();
                if (c == '\\')
                {
                    char esc = Next(json, ref pos);
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (pos + 4 > json.Length)
                                throw new FormatException("Truncated \\u escape");
                            sb.Append((char)int.Parse(json.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            pos += 4;
                            break;
                        default:
                            throw new FormatException($"Invalid escape '\\{esc}'");
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
        }

        private static void SkipWhitespace(string json, ref int pos)
        {
            while (pos < json.Length && char.IsWhiteSpace(json[pos]))
                pos++;
        }

        private static char Peek(string json, int pos)
        {
            if (pos >= json.Length)
                throw new FormatException("Unexpected end of JSON");
            return json[pos];
        }

        private static char Next(string json, ref int pos)
        {
            char c = Peek(json, pos);
            pos++;
            return c;
        }

        private static void Expect(string json, ref int pos, char expected)
        {
            char c = Next(json, ref pos);
            if (c != expected)
                throw new FormatException($"Expected '{expected}' but found '{c}' at position {pos - 1}");
        }
    }
}
