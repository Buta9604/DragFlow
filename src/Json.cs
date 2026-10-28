using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DragFlow
{
    /// <summary>
    /// Just enough JSON for the AI helper: write strings safely, read an answer into dictionaries, lists, strings,
    /// numbers, booleans and null. Nesting is limited so a hostile answer cannot exhaust the stack.
    /// </summary>
    public static class Json
    {
        public static string Str(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        public static object Parse(string text)
        {
            int i = 0;
            object v = Value(text, ref i, 0);
            Space(text, ref i);
            if (i != text.Length) throw new FormatException("Unexpected text after the JSON value.");
            return v;
        }

        static void Space(string t, ref int i) { while (i < t.Length && char.IsWhiteSpace(t[i])) i++; }

        static object Value(string t, ref int i, int depth)
        {
            if (depth > 200) throw new FormatException("JSON nested too deeply.");
            Space(t, ref i);
            if (i >= t.Length) throw new FormatException("JSON ends too early.");
            char c = t[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++; Space(t, ref i);
                if (i < t.Length && t[i] == '}') { i++; return d; }
                while (true)
                {
                    Space(t, ref i);
                    if (i >= t.Length || t[i] != '"') throw new FormatException("A name in quotes was expected.");
                    string key = String(t, ref i);
                    Space(t, ref i);
                    if (i >= t.Length || t[i] != ':') throw new FormatException("A colon was expected.");
                    i++;
                    d[key] = Value(t, ref i, depth + 1);
                    Space(t, ref i);
                    if (i < t.Length && t[i] == ',') { i++; continue; }
                    if (i < t.Length && t[i] == '}') { i++; return d; }
                    throw new FormatException("A comma or } was expected.");
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++; Space(t, ref i);
                if (i < t.Length && t[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(t, ref i, depth + 1));
                    Space(t, ref i);
                    if (i < t.Length && t[i] == ',') { i++; continue; }
                    if (i < t.Length && t[i] == ']') { i++; return l; }
                    throw new FormatException("A comma or ] was expected.");
                }
            }
            if (c == '"') return String(t, ref i);
            if (t.Length - i >= 4 && string.CompareOrdinal(t, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (t.Length - i >= 5 && string.CompareOrdinal(t, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (t.Length - i >= 4 && string.CompareOrdinal(t, i, "null", 0, 4) == 0) { i += 4; return null; }
            int start = i;
            while (i < t.Length && "+-0123456789.eE".IndexOf(t[i]) >= 0) i++;
            double num;
            if (i > start && double.TryParse(t.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out num)) return num;
            throw new FormatException("Unexpected character in JSON.");
        }

        static string String(string t, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (i < t.Length)
            {
                char c = t[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= t.Length) break;
                char e = t[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > t.Length) throw new FormatException("Bad \\u escape.");
                        sb.Append((char)int.Parse(t.Substring(i, 4), NumberStyles.HexNumber));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("A quote is never closed.");
        }

        /// <summary>Follow keys and list indexes ("content", 0, "text") into a parsed value; null when anything is missing.</summary>
        public static object Get(object v, params object[] path)
        {
            foreach (object step in path)
            {
                if (step is string)
                {
                    var d = v as Dictionary<string, object>;
                    object next;
                    if (d == null || !d.TryGetValue((string)step, out next)) return null;
                    v = next;
                }
                else
                {
                    var l = v as List<object>;
                    int idx = (int)step;
                    if (l == null || idx < 0 || idx >= l.Count) return null;
                    v = l[idx];
                }
            }
            return v;
        }
    }
}
