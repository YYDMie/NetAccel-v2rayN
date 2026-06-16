using System.Runtime.InteropServices;
using System.Text;

namespace NetAccel.Managed.Storage;

public sealed class WindowsCredentialVault : ICredentialVault
{
    private readonly string _namespace;

    public WindowsCredentialVault(string ns = "NetAccel.WPF")
    {
        _namespace = ns;
    }

    public Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        var targetName = $"{_namespace}:{key}";
        var targetPtr = Marshal.StringToCoTaskMemUni(targetName);
        var secretPtr = Marshal.StringToCoTaskMemUni(value);

        try
        {
            var credential = new CREDENTIAL
            {
                Flags = 0,
                Type = (int)CredType.GENERIC,
                TargetName = targetPtr,
                CredentialBlob = secretPtr,
                CredentialBlobSize = (uint)(Encoding.Unicode.GetByteCount(value)),
                Persist = (int)CredPersist.LOCAL_MACHINE,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                Comment = IntPtr.Zero,
            };

            if (!CredWriteW(ref credential, 0))
            {
                throw new InvalidOperationException(
                    $"CredWrite failed for '{targetName}'. Error: {Marshal.GetLastWin32Error()}");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(targetPtr);
            Marshal.FreeCoTaskMem(secretPtr);
        }

        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var targetName = $"{_namespace}:{key}";
        var targetPtr = Marshal.StringToCoTaskMemUni(targetName);

        try
        {
            if (!CredReadW(targetPtr, (int)CredType.GENERIC, 0, out var credPtr))
            {
                var err = Marshal.GetLastWin32Error();
                if (err == 1168) // ERROR_NOT_FOUND
                {
                    return Task.FromResult<string?>(null);
                }

                throw new InvalidOperationException(
                    $"CredRead failed for '{targetName}'. Error: {err}");
            }

            try
            {
                var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
                {
                    return Task.FromResult<string?>(null);
                }

                var value = Marshal.PtrToStringUni(cred.CredentialBlob, (int)(cred.CredentialBlobSize / sizeof(char)));
                return Task.FromResult<string?>(value);
            }
            finally
            {
                CredFree(credPtr);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(targetPtr);
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var targetName = $"{_namespace}:{key}";
        var targetPtr = Marshal.StringToCoTaskMemUni(targetName);

        try
        {
            if (!CredDeleteW(targetPtr, (int)CredType.GENERIC, 0))
            {
                var err = Marshal.GetLastWin32Error();
                if (err == 1168) // ERROR_NOT_FOUND
                {
                    return Task.CompletedTask;
                }

                throw new InvalidOperationException(
                    $"CredDelete failed for '{targetName}'. Error: {err}");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(targetPtr);
        }

        return Task.CompletedTask;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        return await GetAsync(key, ct) is not null;
    }

    #region P/Invoke

    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredReadW(
        IntPtr targetName,
        int type,
        int reservedFlag,
        out IntPtr credential);

    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWriteW(
        [In] ref CREDENTIAL credential,
        int flags);

    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDeleteW(
        IntPtr targetName,
        int type,
        int flags);

    [DllImport("Advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    private enum CredType : int
    {
        GENERIC = 1,
        DOMAIN_PASSWORD = 2,
        DOMAIN_CERTIFICATE = 3,
        DOMAIN_VISIBLE_PASSWORD = 4,
        GENERIC_CERTIFICATE = 5,
        DOMAIN_EXTENDED = 6,
        MAXIMUM = 7,
    }

    private enum CredPersist : int
    {
        SESSION = 1,
        LOCAL_MACHINE = 2,
        ENTERPRISE = 3,
    }

    #endregion
}
