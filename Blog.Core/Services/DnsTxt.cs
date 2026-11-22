using System.Text;

namespace Blog.Core.Services;

/// <summary>
/// Builds a DNS question for a name's TXT records and reads the answer. .NET can resolve addresses but
/// has no call for TXT records, and the sender check (SPF, DKIM, DMARC) needs exactly those, so this
/// speaks the wire format directly instead of adding a package for one question type. Pure: bytes in,
/// bytes out. The transport (UDP, then TCP when an answer is cut short) lives in Blog.Web.
/// </summary>
public static class DnsTxt
{
    public const ushort TypeTxt = 16;

    /// <summary>A standard recursive query for the TXT records of <paramref name="name"/>, announcing a 1232-byte UDP buffer.</summary>
    public static byte[] BuildQuery(string name, ushort id)
    {
        var labels = name.Trim().TrimEnd('.').Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length == 0 || labels.Any(l => l.Length > 63 || l.Any(c => c > 127)))
            throw new ArgumentException("Not a plain ASCII domain name.", nameof(name));

        var bytes = new List<byte>(64)
        {
            (byte)(id >> 8), (byte)id,
            0x01, 0x00,             // recursion desired
            0x00, 0x01,             // one question
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x01,             // one additional record (EDNS)
        };
        foreach (var label in labels)
        {
            bytes.Add((byte)label.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(label));
        }
        bytes.Add(0);
        bytes.AddRange([0x00, (byte)TypeTxt, 0x00, 0x01]);                       // TXT, class IN
        bytes.AddRange([0x00, 0x00, 0x29, 0x04, 0xD0, 0, 0, 0, 0, 0x00, 0x00]);   // OPT: root, type 41, UDP size 1232
        return bytes.ToArray();
    }

    public sealed record Answer(bool Truncated, int ResponseCode, IReadOnlyList<string> Texts);

    /// <summary>
    /// The TXT strings in a response to the query with this id, or null when the bytes are not a
    /// response to it. Each record's character-strings are joined, as SPF and DKIM require.
    /// </summary>
    public static Answer? Parse(ReadOnlySpan<byte> message, ushort id)
    {
        try
        {
            if (message.Length < 12 || ((message[0] << 8) | message[1]) != id || (message[2] & 0x80) == 0) return null;
            var truncated = (message[2] & 0x02) != 0;
            var rcode = message[3] & 0x0F;
            var questions = (message[4] << 8) | message[5];
            var answers = (message[6] << 8) | message[7];

            var pos = 12;
            for (var q = 0; q < questions; q++) pos = SkipName(message, pos) + 4;

            var texts = new List<string>();
            for (var a = 0; a < answers; a++)
            {
                pos = SkipName(message, pos);
                var type = (message[pos] << 8) | message[pos + 1];
                var length = (message[pos + 8] << 8) | message[pos + 9];
                pos += 10;
                if (pos + length > message.Length) break;
                if (type == TypeTxt)
                {
                    var sb = new StringBuilder();
                    var end = pos + length;
                    for (var p = pos; p < end;)
                    {
                        var len = message[p++];
                        if (p + len > end) break;
                        sb.Append(Encoding.ASCII.GetString(message.Slice(p, len)));
                        p += len;
                    }
                    texts.Add(sb.ToString());
                }
                pos += length;
            }
            return new Answer(truncated, rcode, texts);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return null;   // a malformed packet is no answer
        }
    }

    private static int SkipName(ReadOnlySpan<byte> message, int pos)
    {
        while (true)
        {
            var len = message[pos];
            if (len == 0) return pos + 1;
            if ((len & 0xC0) == 0xC0) return pos + 2;   // compression pointer ends the name
            pos += len + 1;
        }
    }
}
