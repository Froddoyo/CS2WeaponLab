using System.Security.Cryptography;
using SteamDatabase.ValvePak;

namespace WeaponLab.Core.Game;

/// <summary>
/// Read-only access to a game VPK. Keeps the directory open so repeated
/// reads (clip details, imports) do not re-parse it. Safe to share between
/// threads.
/// </summary>
public sealed class GameArchive : IDisposable
{
    private readonly Package _package = new();
    private readonly Lock _readLock = new();

    public GameArchive(string vpkPath)
    {
        VpkPath = vpkPath;
        _package.Read(vpkPath);
    }

    public string VpkPath { get; }

    public IEnumerable<string> AllPaths()
    {
        return _package.Entries?.SelectMany(group => group.Value).Select(entry => entry.GetFullPath()) ?? [];
    }

    public bool Contains(string path) => _package.FindEntry(path) is not null;

    public byte[] ReadBytes(string path)
    {
        var entry = _package.FindEntry(path) ?? throw new FileNotFoundException($"Not in the game VPK: {path}", path);
        lock (_readLock)
        {
            _package.ReadEntry(entry, out byte[] bytes);
            return bytes;
        }
    }

    public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public void Dispose() => _package.Dispose();
}
