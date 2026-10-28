using System.Text;
using LmCode;

namespace Sample;

static class Protocol
{
    public static void EntryResult(string name, Value value)
    {
        var output = Console.OpenStandardOutput();
        output.Write(Encoding.UTF8.GetBytes("entry " + name + " = "));
        if (value.Kind == ValueKind.Str) output.Write(value.AsStr().Span);
        else output.Write(Encoding.ASCII.GetBytes(value.Kind switch
        {
            ValueKind.Int => value.AsInt().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ValueKind.Float => Formatting.FormatG15(value.AsFloat()),
            ValueKind.Bool => value.AsBool() ? "true" : "false",
            _ => ""
        }));
        output.WriteByte((byte)'\n');
    }

    // JSON syntax is ASCII; bytes >= 0x20 pass through unchanged, matching the
    // reference host. Decoding a VmString first would replace invalid UTF-8.
    public static int Success(Ledger ledger, int entriesCalled)
    {
        using var bytes = new MemoryStream();
        bytes.Write(Encoding.ASCII.GetBytes($"{{\"ok\": true, \"result\": {{\"balance\": {ledger.Balance}, \"entries\": {ledger.Entries}, \"note\": \""));
        foreach (byte b in ledger.Note.Span)
        {
            string? escaped = b switch
            {
                (byte)'"' => "\\\"",
                (byte)'\\' => "\\\\",
                (byte)'\n' => "\\n",
                (byte)'\r' => "\\r",
                (byte)'\t' => "\\t",
                < 0x20 => "\\u" + b.ToString("x4"),
                _ => null
            };
            if (escaped is null) bytes.WriteByte(b);
            else bytes.Write(Encoding.ASCII.GetBytes(escaped));
        }
        bytes.Write(Encoding.ASCII.GetBytes("\", \"entries_called\": " + entriesCalled + "}}\n"));
        bytes.Position = 0;
        bytes.CopyTo(Console.OpenStandardOutput());
        return 0;
    }
}
