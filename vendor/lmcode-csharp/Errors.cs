using System.Text;
using System.Globalization;
namespace LmCode;
public sealed class Error : Exception
{
    public string Code { get; }
    private string message;
    public override string Message => message;
    public uint? Line { get; private set; }
    public ushort? Column { get; private set; }
    public Error(string code, string message) : base(message) { Code = code; this.message = message; }
    internal static Error BadBytecode(string message) => new("LOAD_BAD_BYTECODE", message);
    public static Error Host(string message) => new("RUN_HOST", message);
    public void Locate(uint line, ushort column)
    {
        if (Line.HasValue || line == 0) return;
        Line = line; Column = column == 0 ? null : column;
        message = $"dsl line {line}{(column == 0 ? "" : ":" + column)}: {message}";
    }
}
public static class Reports
{
    public static string JsonEscape(string value)
    {
        var b = new StringBuilder("\"");
        foreach (char c in value) b.Append(c switch
        {
            '"' => "\\\"",
            '\\' => "\\\\",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            < ' ' => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture),
            _ => c.ToString()
        });
        return b.Append('"').ToString();
    }
    public static string ErrorJson(string phase, Error e) => $"{{\"ok\": false, \"phase\": {JsonEscape(phase)}, \"code\": {JsonEscape(e.Code)}, \"message\": {JsonEscape(e.Message)}" +
        (e.Line.HasValue ? $", \"line\": {e.Line}" + (e.Column.HasValue ? $", \"column\": {e.Column}" : "") : "") + "}";
    public static int ReportOk(string resultJson) { Console.WriteLine($"{{\"ok\": true, \"result\": {resultJson}}}"); return 0; }
    public static int ReportError(string phase, Error e) { Console.WriteLine(ErrorJson(phase, e)); return e.Code == "APP_FAIL" ? 4 : 3; }
    public static int ReportAppFailure(string message) => ReportError("app", new Error("APP_FAIL", message));
}
