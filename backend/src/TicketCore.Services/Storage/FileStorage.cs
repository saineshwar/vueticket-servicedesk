using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TicketCore.Common;

namespace TicketCore.Services.Storage;

/// <summary>Where uploaded files live. Local disk today; an S3-compatible store plugs in behind the same interface.</summary>
public interface IFileStorage
{
    /// <summary>Stores the stream under a new random key and returns the key.</summary>
    Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}

public sealed partial class LocalFileStorage(IOptions<StorageOptions> options, TimeProvider clock) : IFileStorage
{
    // Keys are always generated here: yyyy/MM/<32 hex>.<ext>. Anything else is rejected, so a key can never escape the root.
    [GeneratedRegex(@"^\d{4}/\d{2}/[0-9a-f]{32}(\.[a-z0-9]{1,8})?$")]
    private static partial Regex KeyPattern();

    private readonly string _root = Path.GetFullPath(options.Value.LocalPath);

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var ext = extension.TrimStart('.').ToLowerInvariant();
        var key = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{(ext.Length > 0 ? "." + ext : string.Empty)}";
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, ct);
        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) =>
        Task.FromResult<Stream>(new FileStream(PathFor(key), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        if (!KeyPattern().IsMatch(key)) throw new ArgumentException("Invalid storage key.", nameof(key));
        return Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));
    }
}
