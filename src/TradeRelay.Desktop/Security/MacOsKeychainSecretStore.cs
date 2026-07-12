using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TradeRelay.Desktop.Security;

internal sealed class MacOsKeychainSecretStore : IProtectedSecretStore
{
    private const string Service = "TradeRelay";
    private const int Success = 0;
    private const int ItemNotFound = -25300;
    private const int DuplicateItem = -25299;

    public bool CanPersist => OperatingSystem.IsMacOS();

    public Task SaveAsync(string id, string value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAvailable();
        byte[] service = Encoding.UTF8.GetBytes(Service);
        byte[] account = Encoding.UTF8.GetBytes(id);
        byte[] secret = Encoding.UTF8.GetBytes(value);
        int status = Native.SecKeychainAddGenericPassword(IntPtr.Zero, (uint)service.Length, service, (uint)account.Length, account, (uint)secret.Length, secret, out IntPtr item);
        if (status == DuplicateItem)
        {
            status = Native.SecKeychainFindGenericPassword(IntPtr.Zero, (uint)service.Length, service, (uint)account.Length, account, out _, out _, out item);
            if (status == Success)
            {
                status = Native.SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)secret.Length, secret);
            }
        }

        Release(item);
        CryptographicOperations.ZeroMemory(secret);
        ThrowIfFailed(status, "save");
        return Task.CompletedTask;
    }

    public Task<string?> LoadAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAvailable();
        byte[] service = Encoding.UTF8.GetBytes(Service);
        byte[] account = Encoding.UTF8.GetBytes(id);
        int status = Native.SecKeychainFindGenericPassword(IntPtr.Zero, (uint)service.Length, service, (uint)account.Length, account, out uint length, out IntPtr data, out IntPtr item);
        try
        {
            if (status == ItemNotFound) return Task.FromResult<string?>(null);
            ThrowIfFailed(status, "load");
            byte[] value = new byte[length];
            Marshal.Copy(data, value, 0, value.Length);
            string result = Encoding.UTF8.GetString(value);
            CryptographicOperations.ZeroMemory(value);
            return Task.FromResult<string?>(result);
        }
        finally
        {
            if (data != IntPtr.Zero) Native.SecKeychainItemFreeContent(IntPtr.Zero, data);
            Release(item);
        }
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAvailable();
        byte[] service = Encoding.UTF8.GetBytes(Service);
        byte[] account = Encoding.UTF8.GetBytes(id);
        int status = Native.SecKeychainFindGenericPassword(IntPtr.Zero, (uint)service.Length, service, (uint)account.Length, account, out _, out IntPtr data, out IntPtr item);
        if (data != IntPtr.Zero) Native.SecKeychainItemFreeContent(IntPtr.Zero, data);
        if (status == ItemNotFound) return Task.CompletedTask;
        ThrowIfFailed(status, "delete");
        status = Native.SecKeychainItemDelete(item);
        Release(item);
        ThrowIfFailed(status, "delete");
        return Task.CompletedTask;
    }

    private void EnsureAvailable()
    {
        if (!CanPersist) throw new PlatformNotSupportedException("macOS Keychain is unavailable.");
    }

    private static void ThrowIfFailed(int status, string operation)
    {
        if (status != Success) throw new InvalidOperationException($"The system Keychain could not {operation} the protected value (status {status}).");
    }

    private static void Release(IntPtr item)
    {
        if (item != IntPtr.Zero) Native.CFRelease(item);
    }

    private static class Native
    {
        private const string Security = "/System/Library/Frameworks/Security.framework/Security";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        [DllImport(Security)] internal static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceLength, byte[] serviceName, uint accountLength, byte[] accountName, uint passwordLength, byte[] passwordData, out IntPtr itemRef);
        [DllImport(Security)] internal static extern int SecKeychainFindGenericPassword(IntPtr keychainOrArray, uint serviceLength, byte[] serviceName, uint accountLength, byte[] accountName, out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);
        [DllImport(Security)] internal static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length, byte[] data);
        [DllImport(Security)] internal static extern int SecKeychainItemDelete(IntPtr itemRef);
        [DllImport(Security)] internal static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);
        [DllImport(CoreFoundation)] internal static extern void CFRelease(IntPtr value);
    }
}
