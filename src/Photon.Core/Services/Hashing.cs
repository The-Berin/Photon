using System.Security.Cryptography;

namespace Photon.Core.Services;

/// <summary>SHA-256 content hashing with prompt cancellation (1 MiB chunks).</summary>
internal static class Hashing
{
    private const int BufferSize = 1 << 20;

    public static string ComputeSha256(string path, CancellationToken ct, Action<long>? chunkRead = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
        var buffer = new byte[BufferSize];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
            chunkRead?.Invoke(read);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
