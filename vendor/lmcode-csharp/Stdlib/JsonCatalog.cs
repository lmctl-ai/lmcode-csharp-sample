using System.Globalization;
using System.Text;

namespace LmCode.Stdlib;

/// <summary>Strict, byte-oriented JSON catalog. Nodes retain their source token spelling.</summary>
public static class JsonCatalog
{
    private sealed class Node
    {
        public string Kind = "null", Raw = "null";
        public VmString Text = VmString.FromString("");
        public long Integer;
        public double Float;
        public bool Bool;
        public readonly List<Node> Array = [];
        public readonly List<(VmString Key, string RawKey, Node Value)> Object = [];
    }

    private ref struct Parser
    {
        private readonly ReadOnlySpan<byte> input;
        private int p;
        public Parser(ReadOnlySpan<byte> input) => this.input = input;
        private void Fail(string message, int at) => throw Error.Host($"json_parse: {message} at byte {at}");
        private void Skip() { while (p < input.Length && input[p] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') p++; }
        private bool Take(byte c) { if (p < input.Length && input[p] == c) { p++; return true; } return false; }
        private void Literal(ReadOnlySpan<byte> value, string message) { if (p + value.Length > input.Length || !input.Slice(p, value.Length).SequenceEqual(value)) Fail(message, p); p += value.Length; }
        private static int Hex(byte c) => c switch { >= (byte)'0' and <= (byte)'9' => c - '0', >= (byte)'a' and <= (byte)'f' => c - 'a' + 10, >= (byte)'A' and <= (byte)'F' => c - 'A' + 10, _ => -1 };
        private static void PutUtf8(List<byte> o, int cp)
        { if (cp <= 0x7f) o.Add((byte)cp); else if (cp <= 0x7ff) { o.Add((byte)(0xc0 | cp >> 6)); o.Add((byte)(0x80 | (cp & 63))); } else if (cp <= 0xffff) { o.Add((byte)(0xe0 | cp >> 12)); o.Add((byte)(0x80 | (cp >> 6 & 63))); o.Add((byte)(0x80 | (cp & 63))); } else { o.Add((byte)(0xf0 | cp >> 18)); o.Add((byte)(0x80 | (cp >> 12 & 63))); o.Add((byte)(0x80 | (cp >> 6 & 63))); o.Add((byte)(0x80 | (cp & 63))); } }
        private (VmString Value, string Raw) String()
        {
            int start = p; if (!Take((byte)'"')) Fail("expected string", p); var output = new List<byte>();
            while (p < input.Length)
            {
                byte c = input[p++]; if (c == '"') return (new VmString(output.ToArray()), Encoding.UTF8.GetString(input[start..p]));
                if (c < 32) Fail("control byte in string", p - 1);
                if (c != '\\') { output.Add(c); continue; }
                if (p >= input.Length) Fail("unterminated escape", p);
                byte e = input[p++]; switch (e)
                {
                    case (byte)'"': case (byte)'\\': case (byte)'/': output.Add(e); break;
                    case (byte)'b': output.Add(8); break; case (byte)'f': output.Add(12); break; case (byte)'n': output.Add(10); break; case (byte)'r': output.Add(13); break; case (byte)'t': output.Add(9); break;
                    case (byte)'u':
                        int cp = Codepoint(); if (cp is >= 0xd800 and <= 0xdbff) { if (p + 6 > input.Length || input[p] != '\\' || input[p + 1] != 'u') Fail("unpaired high surrogate", p); p += 2; int low = Codepoint(); if (low is < 0xdc00 or > 0xdfff) Fail("unpaired high surrogate", p - 4); cp = 0x10000 + ((cp - 0xd800) << 10) + low - 0xdc00; } else if (cp is >= 0xdc00 and <= 0xdfff) Fail("unpaired low surrogate", p - 4); PutUtf8(output, cp); break;
                    default: Fail("invalid escape", p - 1); break;
                }
            }
            Fail("unterminated string", p); return default;
        }
        private int Codepoint()
        { if (p + 4 > input.Length) Fail("invalid Unicode escape", p); int cp = 0; for (int i = 0; i < 4; i++) { int h = Hex(input[p + i]); if (h < 0) Fail("invalid Unicode escape", p + i); cp = cp * 16 + h; } p += 4; return cp; }
        private Node Number()
        {
            int start = p; if (input[p] == '-') p++; if (p >= input.Length || input[p] is < (byte)'0' or > (byte)'9') Fail("invalid number", p);
            if (input[p] == '0') p++; else while (p < input.Length && input[p] is >= (byte)'0' and <= (byte)'9') p++;
            bool floating = false; if (p < input.Length && input[p] == '.') { floating = true; p++; if (p >= input.Length || input[p] is < (byte)'0' or > (byte)'9') Fail("invalid number", p); while (p < input.Length && input[p] is >= (byte)'0' and <= (byte)'9') p++; }
            if (p < input.Length && input[p] is (byte)'e' or (byte)'E') { floating = true; p++; if (p < input.Length && input[p] is (byte)'+' or (byte)'-') p++; if (p >= input.Length || input[p] is < (byte)'0' or > (byte)'9') Fail("invalid number", p); while (p < input.Length && input[p] is >= (byte)'0' and <= (byte)'9') p++; }
            string raw = Encoding.ASCII.GetString(input[start..p]); var node = new Node { Kind = floating ? "float" : "int", Raw = raw };
            if (!floating) { if (!long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out node.Integer)) Fail("integer out of range", start); }
            else if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out node.Float) || !double.IsFinite(node.Float)) Fail("nonfinite number", start);
            return node;
        }
        public Node Value(int depth = 0)
        {
            Skip(); if (p >= input.Length) Fail("expected value", p); int start = p; Node node;
            switch (input[p])
            {
                case (byte)'n': Literal("null"u8, "invalid literal"); node = new Node(); break;
                case (byte)'t': Literal("true"u8, "invalid literal"); node = new Node { Kind = "bool", Bool = true, Raw = "true" }; break;
                case (byte)'f': Literal("false"u8, "invalid literal"); node = new Node { Kind = "bool", Raw = "false" }; break;
                case (byte)'"': var str = String(); node = new Node { Kind = "string", Text = str.Value, Raw = str.Raw }; break;
                case (byte)'[': if (++depth > 256) Fail("maximum depth exceeded", p); p++; node = new Node { Kind = "array", Raw = "[]" }; Skip(); if (!Take((byte)']')) { while (true) { node.Array.Add(Value(depth)); Skip(); if (Take((byte)']')) break; if (!Take((byte)',')) Fail("expected comma", p); } } break;
                case (byte)'{': if (++depth > 256) Fail("maximum depth exceeded", p); p++; node = new Node { Kind = "object", Raw = "{}" }; Skip(); if (!Take((byte)'}')) { while (true) { Skip(); int keyStart = p; if (p >= input.Length || input[p] != '"') Fail("object key must be a string", p); var key = String(); if (node.Object.Any(x => x.Key.Equals(key.Value))) Fail("duplicate object key", keyStart); Skip(); if (!Take((byte)':')) Fail("expected colon", p); node.Object.Add((key.Value, key.Raw, Value(depth))); Skip(); if (Take((byte)'}')) break; if (!Take((byte)',')) Fail("expected comma", p); } } break;
                default: if (input[p] == '-' || input[p] is >= (byte)'0' and <= (byte)'9') node = Number(); else { Fail("unexpected value", p); node = new Node(); } break;
            }
            return node;
        }
        public Node Parse() { Node n = Value(); Skip(); if (p != input.Length) Fail("trailing input", p); return n; }
    }


    private const string TemplatePath = @"[A-Za-z_][A-Za-z0-9_]*(?:[.][A-Za-z_][A-Za-z0-9_]*|\[[0-9]+\])*";
    private static readonly System.Text.RegularExpressions.Regex TemplateHead = new("^<("+TemplatePath+@")(?::\{([A-Za-z_][A-Za-z0-9_]*)\||>)");
    private static readonly System.Text.RegularExpressions.Regex TemplateIf = new(@"^<if\(("+TemplatePath+@")\)>");
    private static readonly System.Text.RegularExpressions.Regex TemplatePart = new(@"[A-Za-z_][A-Za-z0-9_]*|\[[0-9]+\]");
    private static readonly System.Text.RegularExpressions.Regex TemplateClose = new("^};[ ]*separator[ ]*=[ ]*(\"(?:[^\"\\\\]|\\\\.)*\")>");
    private sealed class TemplateNode {
        public string Text="",Path="",Item="",Separator="";
        public bool Conditional;
        public List<TemplateNode> Body=[],Otherwise=[];
    }
    private sealed class TemplateParser(string source) {
        private int pos;
        public string Ending="";
        private static void Flush(List<TemplateNode> nodes,StringBuilder literal){
            if(literal.Length>0){nodes.Add(new TemplateNode{Text=literal.ToString()});literal.Clear();}
        }
        public List<TemplateNode> Parse(string mode,int depth){
            if(depth>256)throw Error.Host("template_render: maximum nesting exceeded");
            List<TemplateNode> nodes=[];var literal=new StringBuilder();
            while(pos<source.Length){
                string rest=source[pos..];
                if(mode=="loop" && rest.StartsWith("}>",StringComparison.Ordinal)){Flush(nodes,literal);pos+=2;Ending="";return nodes;}
                if(mode=="loop" && rest.StartsWith("};",StringComparison.Ordinal)){
                    var m=TemplateClose.Match(rest);if(!m.Success)throw Error.Host("template_render: invalid loop separator");
                    byte[] raw=Encoding.Latin1.GetBytes(m.Groups[1].Value);
                    try{
                        _=new UTF8Encoding(false,true).GetString(raw);
                        Ending=Encoding.Latin1.GetString(new Parser(raw).Parse().Text.Span);
                    }catch(Exception e) when(e is Error or DecoderFallbackException){throw Error.Host("template_render: invalid loop separator");}
                    Flush(nodes,literal);pos+=m.Length;return nodes;
                }
                if(rest.StartsWith("<<",StringComparison.Ordinal)){literal.Append('<');pos+=2;continue;}
                if(rest.StartsWith("<else>",StringComparison.Ordinal) || rest.StartsWith("<endif>",StringComparison.Ordinal)){
                    string tag=rest.StartsWith("<else>",StringComparison.Ordinal)?"else":"endif";
                    if((mode!="if" && mode!="else") || (mode=="else" && tag=="else"))throw Error.Host("template_render: unexpected conditional delimiter");
                    Flush(nodes,literal);pos+=tag=="else"?6:7;Ending=tag;return nodes;
                }
                var conditional=TemplateIf.Match(rest);
                if(conditional.Success){
                    Flush(nodes,literal);pos+=conditional.Length;var n=new TemplateNode{Path=conditional.Groups[1].Value,Conditional=true};
                    n.Body=Parse("if",depth+1);if(Ending=="else")n.Otherwise=Parse("else",depth+1);nodes.Add(n);continue;
                }
                var head=TemplateHead.Match(rest);
                if(!head.Success){literal.Append(source[pos++]);continue;}
                Flush(nodes,literal);pos+=head.Length;var node=new TemplateNode{Path=head.Groups[1].Value,Item=head.Groups[2].Value};
                if(node.Item!=""){
                    if(node.Item=="index")throw Error.Host("template_render: index is reserved");
                    node.Body=Parse("loop",depth+1);node.Separator=Ending;
                }
                nodes.Add(node);
            }
            if(mode!="")throw Error.Host("template_render: unterminated block");
            Flush(nodes,literal);return nodes;
        }
    }
    private static string TemplateJson(Node n)=>Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(Emit(n)));
    private static Node TemplateLookup(Node context,Dictionary<string,Node> locals,string path){
        Node n=context;bool first=true;
        foreach(System.Text.RegularExpressions.Match part in TemplatePart.Matches(path)){
            string key=part.Value;
            if(first){first=false;if(locals.TryGetValue(key,out var local)){n=local;continue;}}
            Node? next=null;
            if(key.StartsWith('[')){
                string digits=key[1..^1];
                if(n.Kind=="array" && int.TryParse(digits,NumberStyles.None,CultureInfo.InvariantCulture,out int i)
                    && i>=0 && i<n.Array.Count && i.ToString(CultureInfo.InvariantCulture)==digits)next=n.Array[i];
            }else if(n.Kind=="object")foreach(var m in n.Object)if(Encoding.Latin1.GetString(m.Key.Span)==key){next=m.Value;break;}
            if(next is null)throw Error.Host("template_render: missing variable "+path);n=next;
        }
        return n;
    }
    private static void EvaluateTemplate(List<TemplateNode> nodes,Node context,Dictionary<string,Node> locals,StringBuilder output){
        foreach(var n in nodes){
            if(n.Path==""){output.Append(n.Text);continue;}
            Node value=TemplateLookup(context,locals,n.Path);
            if(n.Conditional){
                if(value.Kind!="bool")throw Error.Host("template_render: condition requires a bool: "+n.Path);
                EvaluateTemplate(value.Bool?n.Body:n.Otherwise,context,locals,output);continue;
            }
            if(n.Item==""){output.Append(value.Kind=="string"?Encoding.Latin1.GetString(value.Text.Span):TemplateJson(value));continue;}
            if(value.Kind!="array")throw Error.Host("template_render: loop requires a list: "+n.Path);
            var scope=new Dictionary<string,Node>(locals);
            for(int i=0;i<value.Array.Count;i++){
                if(i>0)output.Append(n.Separator);
                scope[n.Item]=value.Array[i];scope["index"]=new Node{Kind="int",Integer=i,Raw=i.ToString(CultureInfo.InvariantCulture)};
                EvaluateTemplate(n.Body,context,scope,output);
            }
        }
    }
    private static string RenderTemplate(string source,Node context){
        var nodes=new TemplateParser(source).Parse("",0);var output=new StringBuilder();
        EvaluateTemplate(nodes,context,[],output);return output.ToString();
    }

    public static void RegisterJson(Registry r)
    {
        r.BeginCompatibilityRegistration();
        try
        {
            r.Function("json_parse", ["string"], "Json", (c, a) =>
            {
                var bytes = a[0].AsStr().Span;
                try { _ = new UTF8Encoding(false, true).GetString(bytes); } catch (DecoderFallbackException ex) { throw Error.Host($"json_parse: invalid UTF-8 at byte {ex.Index}"); }
                if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf) throw Error.Host("json_parse: BOM is not allowed at byte 0");
                return Value.Object("Json", CollectionsCatalog.Allocate(c, "Json", new Parser(bytes).Parse()));
            });
            r.Function("template_render", ["string","Json"], "string", (c,a) => {
                var context=c.Get<Node>(a[1].AsObj());
                if(context.Kind!="object")throw Error.Host("template_render: context must be an object");
                return Value.Str(new VmString(Encoding.Latin1.GetBytes(RenderTemplate(Encoding.Latin1.GetString(a[0].AsStr().Span),context))));
            });
            r.Method("Json", "kind", [], "string", (c, o, _) => Text(c.Get<Node>(o).Kind));
            r.Method("Json", "len", [], "int", (c, o, _) => { var n = c.Get<Node>(o); if (n.Kind is not ("array" or "object")) throw Error.Host("Json.len: expected array or object"); return Value.Int(n.Kind == "array" ? n.Array.Count : n.Object.Count); });
            r.Method("Json", "at", ["int"], "Json", (c, o, a) => { var n = c.Get<Node>(o); if (n.Kind != "array") throw Error.Host("Json.at: expected array"); long i = a[0].AsInt(); if (i < 0 || i >= n.Array.Count) throw Error.Host($"Json.at: index out of range: {i}"); return Value.Object("Json", CollectionsCatalog.Borrow(c, "Json", n.Array[(int)i])); });
            r.Method("Json", "get", ["string"], "Json", (c, o, a) => { var n = c.Get<Node>(o); if (n.Kind != "object") throw Error.Host("Json.get: expected object"); foreach (var x in n.Object) if (x.Key.Equals(a[0].AsStr())) return Value.Object("Json", CollectionsCatalog.Borrow(c, "Json", x.Value)); throw Error.Host("Json.get: missing key"); });
            r.Method("Json", "has", ["string"], "bool", (c, o, a) => { var n = c.Get<Node>(o); if (n.Kind != "object") throw Error.Host("Json.has: expected object"); return Value.Bool(n.Object.Any(x => x.Key.Equals(a[0].AsStr()))); });
            r.Method("Json", "keys", [], "StrList", (c, o, _) => { var n = c.Get<Node>(o); if (n.Kind != "object") throw Error.Host("Json.keys: expected object"); return Value.Object("StrList", CollectionsCatalog.AllocateStrList(c, new StrList(n.Object.Select(x => x.Key)))); });
            r.Method("Json", "as_string", [], "string", (c, o, _) => Scalar(c.Get<Node>(o), "string", n => Value.Str(n.Text)));
            r.Method("Json", "as_int", [], "int", (c, o, _) => Scalar(c.Get<Node>(o), "int", n => Value.Int(n.Integer)));
            r.Method("Json", "as_float", [], "float", (c, o, _) => { var n = c.Get<Node>(o); if (n.Kind == "float") return Value.Float(n.Float); if (n.Kind == "int") return Value.Float(n.Integer); throw Error.Host("Json.as_float: expected number"); });
            r.Method("Json", "as_bool", [], "bool", (c, o, _) => Scalar(c.Get<Node>(o), "bool", n => Value.Bool(n.Bool)));
            r.Method("Json", "to_json", [], "string", (c, o, _) => Value.Str(VmString.FromString(Emit(c.Get<Node>(o)))));
        }
        finally { r.EndCompatibilityRegistration(); }
    }
    private static Value Text(string x) => Value.Str(VmString.FromString(x));
    private static Value Scalar(Node n, string expected, Func<Node, Value> ok) => n.Kind == expected ? ok(n) : throw Error.Host($"Json.as_{expected}: expected {expected}");
    private static string Emit(Node n) => n.Kind switch { "null" or "bool" or "int" or "float" or "string" => n.Raw, "array" => "[" + string.Join(',', n.Array.Select(Emit)) + "]", "object" => "{" + string.Join(',', n.Object.Select(x => x.RawKey + ":" + Emit(x.Value))) + "}", _ => "null" };
}
