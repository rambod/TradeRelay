using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;

namespace TradeRelay.Desktop.Security;

[SupportedOSPlatform("windows")]
internal sealed class WindowsProtectedSecretStore(string directory) : IProtectedSecretStore
{
    public bool CanPersist => OperatingSystem.IsWindows();

    public async Task SaveAsync(string id, string value, CancellationToken cancellationToken)
    {
        EnsureAvailable();
        Directory.CreateDirectory(directory);
        byte[] clear = Encoding.UTF8.GetBytes(value);
        byte[] encrypted = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser);
        CryptographicOperations.ZeroMemory(clear);
        string path = GetPath(id);
        string temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, encrypted, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, true);
    }

    public async Task<string?> LoadAsync(string id, CancellationToken cancellationToken)
    {
        EnsureAvailable();
        string path = GetPath(id);
        if (!File.Exists(path)) return null;
        byte[] encrypted = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        byte[] clear = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        string value = Encoding.UTF8.GetString(clear);
        CryptographicOperations.ZeroMemory(clear);
        return value;
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        EnsureAvailable();
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(GetPath(id));
        return Task.CompletedTask;
    }

    private string GetPath(string id) => Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".bin");
    private void EnsureAvailable() { if (!CanPersist) throw new PlatformNotSupportedException("Windows DPAPI is unavailable."); }
}
