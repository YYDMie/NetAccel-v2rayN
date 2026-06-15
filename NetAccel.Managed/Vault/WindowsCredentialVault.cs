using System.Runtime.InteropServices;
using System.Text;

namespace NetAccel.Managed.Vault;

/// <summary>
/// Windows Credential Manager backed vault.
/// Target names are namespaced to avoid cross-product collision.
/// </summary>
public sealed class WindowsCredentialVault : ICredentialVault, IDisposable
{
    private readonly string _namespace;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public WindowsCredentialVault(string? appNamespace = null)
    {
        _namespace = appNamespace ?? "NetAccel/WPF";
    }

    private string TargetName(CredentialVaultEntry entry) => $"{_namespace}/{entry}";

    public async Task StoreAsync(CredentialVaultEntry entry, string value)
    {
        await _lock.WaitAsync();
        try
        {
            var target = TargetName(entry);
            var bytes = Encoding.UTF8.GetBytes(value);
            var cred = new NativeMethods.Credential
            {
                Type = NativeMethods.CredentialType.Generic,
                TargetName = Marshal.StringToCoTaskMemUni(target),
                CredentialBlob = Marshal.AllocCoTaskMem(bytes.Length),
                CredentialBlobSize = (uint)bytes.Length,
                Persist = NativeMethods.CredentialPersist.LocalMachine,
            };
            try
            {
                Marshal.Copy(bytes, 0, cred.CredentialBlob, bytes.Length);
                if (!NativeMethods.CredWriteW(ref cred, 0))
                {
                    throw new InvalidOperationException($"CredWrite failed for {target}: {Marshal.GetLastWin32Error()}");
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(cred.TargetName);
                Marshal.FreeCoTaskMem(cred.CredentialBlob);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string?> RetrieveAsync(CredentialVaultEntry entry)
    {
        await _lock.WaitAsync();
        try
        {
            var target = TargetName(entry);
            if (!NativeMethods.CredReadW(target, NativeMethods.CredentialType.Generic, 0, out var credPtr))
            {
                var err = Marshal.GetLastWin32Error();
                if (err == 1168) // ERROR_NOT_FOUND
                {
                    return null;
                }
                throw new InvalidOperationException($"CredRead failed for {target}: {err}");
            }

            try
            {
                var cred = Marshal.PtrToStructure<NativeMethods.Credential>(credPtr);
                if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize <= 0)
                {
                    return null;
                }
                var bytes = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, bytes, 0, (int)cred.CredentialBlobSize);
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                NativeMethods.CredFree(credPtr);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteAsync(CredentialVaultEntry entry)
    {
        await _lock.WaitAsync();
        try
        {
            var target = TargetName(entry);
            if (!NativeMethods.CredDeleteW(target, NativeMethods.CredentialType.Generic, 0))
            {
                var err = Marshal.GetLastWin32Error();
                if (err == 1168) // ERROR_NOT_FOUND
                {
                    return;
                }
                throw new InvalidOperationException($"CredDelete failed for {target}: {err}");
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }

    private static class NativeMethods
    {
        [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredWriteW([In] ref Credential userCredential, [In] uint flags);

        [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredReadW(
            [In] string target,
            [In] CredentialType type,
            [In] uint reservedFlag,
            out IntPtr credentialPtr);

        [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredDeleteW([In] string target, [In] CredentialType type, [In] uint flags);

        [DllImport("Advapi32.dll", SetLastError = true)]
        public static extern void CredFree([In] IntPtr buffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct Credential
        {
            public uint Flags;
            public CredentialType Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public CredentialPersist Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }

        public enum CredentialType : uint
        {
            Generic = 1,
        }

        public enum CredentialPersist : uint
        {
            Session = 1,
            LocalMachine = 2,
            Enterprise = 3,
        }
    }
}
