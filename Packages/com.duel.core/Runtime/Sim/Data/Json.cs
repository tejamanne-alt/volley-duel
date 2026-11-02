using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Duel.Core.Sim.Data
{
    public enum JsonKind : byte
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object,
    }

    /// <summary>
    /// Minimal JSON tree for baking data tables. Numbers are kept as their source text and
    /// converted with integer-only parsers (<see cref="Fix64.Parse"/>), so no floating point
    /// is ever involved. Object members keep document order.
    /// </summary>
    public sealed class JsonNode
    {
        public readonly JsonKind Kind;
        readonly string text;
        readonly bool boolValue;
        readonly List<JsonNode> items;
        readonly List<KeyValuePair<string, JsonNode>> members;

        /// <summary>Dotted path of this node in its document, for error messages.</summary>
        public string Path { get; private set; } = "$";

        JsonNode(JsonKind kind, string text = null, bool boolValue = false)
        {
            Kind = kind;
            this.text = text;
            this.boolValue = boolValue;
            if (kind == JsonKind.Array) items = new List<JsonNode>();
            if (kind == JsonKind.Object) members = new List<KeyValuePair<string, JsonNode>>();
        }

        public static JsonNode Parse(string json, string sourceName = "json")
        {
            var parser = new Parser(json, sourceName);
            JsonNode root = parser.ParseValue("$");
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw parser.Error("Unexpected trailing characters");
            return root;
        }

        // ------------------------------------------------------------ access

        public int Count => Kind == JsonKind.Array ? items.Count : (Kind == JsonKind.Object ? members.Count : 0);

        public JsonNode this[int index]
        {
            get
            {
                if (Kind != JsonKind.Array) throw Fail("is not an array");
                if (index < 0 || index >= items.Count) throw Fail("has no element " + index);
                return items[index];
            }
        }

        /// <summary>Required member.</summary>
        public JsonNode this[string key]
        {
            get
            {
                JsonNode n = Get(key);
                if (n == null) throw Fail("is missing required key '" + key + "'");
                return n;
            }
        }

        /// <summary>Optional member, or null.</summary>
        public JsonNode Get(string key)
        {
            if (Kind != JsonKind.Object) throw Fail("is not an object");
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i].Key == key) return members[i].Value;
            }
            return null;
        }

        public bool Has(string key) => Kind == JsonKind.Object && Get(key) != null;

        public IReadOnlyList<KeyValuePair<string, JsonNode>> Members
        {
            get
            {
                if (Kind != JsonKind.Object) throw Fail("is not an object");
                return members;
            }
        }

        public IReadOnlyList<JsonNode> Items
        {
            get
            {
                if (Kind != JsonKind.Array) throw Fail("is not an array");
                return items;
            }
        }

        public string AsString()
        {
            if (Kind != JsonKind.String) throw Fail("is not a string");
            return text;
        }

        public bool AsBool()
        {
            if (Kind != JsonKind.Bool) throw Fail("is not a boolean");
            return boolValue;
        }

        /// <summary>Number as fixed point (accepts numbers and numeric strings such as "2.35").</summary>
        public Fix64 AsFix()
        {
            if (Kind != JsonKind.Number && Kind != JsonKind.String) throw Fail("is not a number");
            if (!Fix64.TryParse(text, out Fix64 v)) throw Fail("is not a decimal number without exponent: '" + text + "'");
            return v;
        }

        public int AsInt()
        {
            if (Kind != JsonKind.Number) throw Fail("is not a number");
            if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int v)) throw Fail("is not an integer: '" + text + "'");
            return v;
        }

        public string NumberText
        {
            get
            {
                if (Kind != JsonKind.Number) throw Fail("is not a number");
                return text;
            }
        }

        // Typed optional getters with defaults.
        public Fix64 Fix(string key, Fix64 fallback)
        {
            JsonNode n = Get(key);
            return n == null ? fallback : n.AsFix();
        }

        public Fix64 Fix(string key) => this[key].AsFix();

        public int Int(string key, int fallback)
        {
            JsonNode n = Get(key);
            return n == null ? fallback : n.AsInt();
        }

        public int Int(string key) => this[key].AsInt();

        public string Str(string key, string fallback)
        {
            JsonNode n = Get(key);
            return n == null ? fallback : n.AsString();
        }

        public string Str(string key) => this[key].AsString();

        public bool Bool(string key, bool fallback)
        {
            JsonNode n = Get(key);
            return n == null ? fallback : n.AsBool();
        }

        public FormatException Fail(string message) => new FormatException(Path + " " + message);

        // ------------------------------------------------------------ parser

        sealed class Parser
        {
            readonly string s;
            readonly string source;
            int i;

            public Parser(string s, string source)
            {
                this.s = s ?? string.Empty;
                this.source = source;
            }

            public bool AtEnd => i >= s.Length;

            public FormatException Error(string message)
            {
                int line = 1, col = 1;
                for (int k = 0; k < i && k < s.Length; k++)
                {
                    if (s[k] == '\n')
                    {
                        line++;
                        col = 1;
                    }
                    else
                    {
                        col++;
                    }
                }
                return new FormatException(source + "(" + line + "," + col + "): " + message);
            }

            public void SkipWhitespace()
            {
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    {
                        i++;
                    }
                    else if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                    {
                        // Line comments are allowed in data files to explain tuning choices.
                        while (i < s.Length && s[i] != '\n') i++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            public JsonNode ParseValue(string path)
            {
                SkipWhitespace();
                if (AtEnd) throw Error("Unexpected end of input");
                char c = s[i];
                JsonNode node;
                if (c == '{') node = ParseObject(path);
                else if (c == '[') node = ParseArray(path);
                else if (c == '"') node = new JsonNode(JsonKind.String, ParseString());
                else if (c == 't') node = Literal("true", new JsonNode(JsonKind.Bool, null, true));
                else if (c == 'f') node = Literal("false", new JsonNode(JsonKind.Bool, null, false));
                else if (c == 'n') node = Literal("null", new JsonNode(JsonKind.Null));
                else if (c == '-' || (c >= '0' && c <= '9')) node = new JsonNode(JsonKind.Number, ParseNumber());
                else throw Error("Unexpected character '" + c + "'");
                node.Path = path;
                return node;
            }

            JsonNode Literal(string word, JsonNode node)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw Error("Expected " + word);
                i += word.Length;
                return node;
            }

            JsonNode ParseObject(string path)
            {
                var node = new JsonNode(JsonKind.Object);
                i++;
                SkipWhitespace();
                if (!AtEnd && s[i] == '}')
                {
                    i++;
                    return node;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || s[i] != '"') throw Error("Expected a string key");
                    string key = ParseString();
                    for (int k = 0; k < node.members.Count; k++)
                    {
                        if (node.members[k].Key == key) throw Error("Duplicate key '" + key + "'");
                    }
                    SkipWhitespace();
                    if (AtEnd || s[i] != ':') throw Error("Expected ':'");
                    i++;
                    JsonNode value = ParseValue(path + "." + key);
                    node.members.Add(new KeyValuePair<string, JsonNode>(key, value));
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unterminated object");
                    if (s[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    if (s[i] == '}')
                    {
                        i++;
                        return node;
                    }
                    throw Error("Expected ',' or '}'");
                }
            }

            JsonNode ParseArray(string path)
            {
                var node = new JsonNode(JsonKind.Array);
                i++;
                SkipWhitespace();
                if (!AtEnd && s[i] == ']')
                {
                    i++;
                    return node;
                }
                while (true)
                {
                    node.items.Add(ParseValue(path + "[" + node.items.Count + "]"));
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unterminated array");
                    if (s[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    if (s[i] == ']')
                    {
                        i++;
                        return node;
                    }
                    throw Error("Expected ',' or ']'");
                }
            }

            string ParseString()
            {
                i++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("Unterminated string");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (AtEnd) throw Error("Unterminated escape");
                    char e = s[i++];
                    switch (e)
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
                            if (i + 4 > s.Length) throw Error("Bad unicode escape");
                            int code = 0;
                            for (int k = 0; k < 4; k++)
                            {
                                char h = s[i++];
                                int d = h >= '0' && h <= '9' ? h - '0' : (h >= 'a' && h <= 'f' ? h - 'a' + 10 : (h >= 'A' && h <= 'F' ? h - 'A' + 10 : -1));
                                if (d < 0) throw Error("Bad unicode escape");
                                code = code * 16 + d;
                            }
                            sb.Append((char)code);
                            break;
                        default:
                            throw Error("Bad escape '\\" + e + "'");
                    }
                }
            }

            string ParseNumber()
            {
                int start = i;
                if (s[i] == '-') i++;
                while (i < s.Length && ((s[i] >= '0' && s[i] <= '9') || s[i] == '.')) i++;
                if (i < s.Length && (s[i] == 'e' || s[i] == 'E')) throw Error("Exponents are not allowed in data numbers");
                return s.Substring(start, i - start);
            }
        }
    }
}
