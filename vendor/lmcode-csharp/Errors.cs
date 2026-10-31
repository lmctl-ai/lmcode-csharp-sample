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
    internal Error Snapshot() => new Error(Code,message) {Line=Line,Column=Column};
    public void Locate(uint line, ushort column)
    {
        if (Line.HasValue || line == 0) return;
        Line = line; Column = column == 0 ? null : column;
        message = $"dsl line {line}{(column == 0 ? "" : ":" + column)}: {message}";
    }
}
