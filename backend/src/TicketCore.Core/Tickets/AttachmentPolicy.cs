using TicketCore.Common;

namespace TicketCore.Core.Tickets;

/// <summary>
/// Which files may be attached. An allow-list of types, a size and count limit, and a check that the first bytes match
/// the extension (so a renamed executable cannot pass as a PDF). Downloads are always served as attachments, never inline.
/// </summary>
public static class AttachmentPolicy
{
    private sealed record FileKind(string ContentType, byte[][] Signatures);

    private static readonly byte[] Pdf = "%PDF"u8.ToArray();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Gif = "GIF8"u8.ToArray();
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04]; // also docx/xlsx/pptx
    private static readonly byte[] Ole = [0xD0, 0xCF, 0x11, 0xE0]; // .msg

    // Empty signature list = text formats, checked for binary content instead.
    private static readonly Dictionary<string, FileKind> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = new("application/pdf", [Pdf]),
        [".png"] = new("image/png", [Png]),
        [".jpg"] = new("image/jpeg", [Jpeg]),
        [".jpeg"] = new("image/jpeg", [Jpeg]),
        [".gif"] = new("image/gif", [Gif]),
        [".zip"] = new("application/zip", [Zip]),
        [".docx"] = new("application/vnd.openxmlformats-officedocument.wordprocessingml.document", [Zip]),
        [".xlsx"] = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [Zip]),
        [".pptx"] = new("application/vnd.openxmlformats-officedocument.presentationml.presentation", [Zip]),
        [".msg"] = new("application/vnd.ms-outlook", [Ole]),
        [".txt"] = new("text/plain", []),
        [".log"] = new("text/plain", []),
        [".csv"] = new("text/csv", []),
        [".eml"] = new("message/rfc822", []),
    };

    public static IReadOnlyCollection<string> AllowedExtensions => Allowed.Keys;

    /// <summary>Returns the content type to store, or an error message written for the uploader.</summary>
    public static (string? ContentType, string? Error) Check(string fileName, long length, ReadOnlySpan<byte> head, StorageOptions limits)
    {
        var name = Path.GetFileName(fileName ?? string.Empty);
        var ext = Path.GetExtension(name);
        if (name.Length is 0 or > 255) return (null, "Each file needs a name of up to 255 characters.");
        if (!Allowed.TryGetValue(ext, out var kind))
            return (null, $"{name}: this file type can't be attached. Allowed: {string.Join(", ", Allowed.Keys.Order())}.");
        if (length <= 0) return (null, $"{name} is empty.");
        if (length > limits.MaxFileBytes)
            return (null, $"{name} is larger than {limits.MaxFileBytes / (1024 * 1024)} MB.");

        if (kind.Signatures.Length > 0)
        {
            var matches = false;
            foreach (var signature in kind.Signatures)
            {
                if (head.Length >= signature.Length && head[..signature.Length].SequenceEqual(signature)) matches = true;
            }
            if (!matches) return (null, $"{name} doesn't look like a real {ext.TrimStart('.').ToUpperInvariant()} file.");
        }
        else if (head.Contains((byte)0))
        {
            return (null, $"{name} doesn't look like a text file.");
        }

        return (kind.ContentType, null);
    }

    /// <summary>A safe download name: the original name without path parts or control characters.</summary>
    public static string SafeName(string fileName)
    {
        var name = Path.GetFileName(fileName ?? "file");
        var cleaned = new string(name.Where(c => !char.IsControl(c) && c is not ('"' or '\\' or '/')).ToArray()).Trim();
        return cleaned.Length == 0 ? "file" : cleaned;
    }
}
