using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Discorder.Core.Security;

public static class AuthenticodeSignatureVerifier
{
    private static readonly Guid GenericVerifyV2 = new(
        "00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public static string GetRequiredSignerThumbprint(string path)
    {
        var thumbprint = TryGetSignerThumbprint(path);
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            throw new InvalidDataException(
                $"{Path.GetFileName(path)} imzalı değil. Güvenli güncelleme için Discorder yayınları imzalı olmalıdır.");
        }

        return thumbprint;
    }

    public static string? TryGetSignerThumbprint(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Authenticode doğrulaması Windows gerektirir.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "İmza doğrulanacak dosya bulunamadı.",
                path);
        }

        try
        {
            VerifyAuthenticode(path);
#pragma warning disable SYSLIB0057
            using var certificate = new X509Certificate2(
                X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return NormalizeThumbprint(certificate.Thumbprint);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    public static void VerifyFile(string path, string expectedSignerThumbprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSignerThumbprint);
        var actualThumbprint = GetRequiredSignerThumbprint(path);
        if (!string.Equals(
                actualThumbprint,
                NormalizeThumbprint(expectedSignerThumbprint),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{Path.GetFileName(path)} beklenen Discorder imzasıyla eşleşmiyor.");
        }
    }

    public static bool ShouldVerify(string relativePath)
    {
        var extension = Path.GetExtension(relativePath);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".dll", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeThumbprint(string thumbprint)
    {
        return thumbprint
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
    }

    private static void VerifyAuthenticode(string path)
    {
        var filePathPointer = IntPtr.Zero;
        var fileInfoPointer = IntPtr.Zero;

        try
        {
            filePathPointer = Marshal.StringToCoTaskMemUni(path);
            var fileInfo = new WinTrustFileInfo
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                FilePath = filePathPointer
            };

            fileInfoPointer = Marshal.AllocHGlobal(
                Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, fDeleteOld: false);

            var trustData = new WinTrustData
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = WinTrustUiChoice.None,
                RevocationChecks = WinTrustRevocationChecks.WholeChain,
                UnionChoice = WinTrustUnionChoice.File,
                FileInfo = fileInfoPointer,
                StateAction = WinTrustStateAction.Ignore,
                ProviderFlags =
                    WinTrustProviderFlags.RevocationCheckChainExcludeRoot,
                UiContext = WinTrustUiContext.Execute
            };

            var result = WinVerifyTrust(
                IntPtr.Zero,
                GenericVerifyV2,
                ref trustData);

            if (result != 0)
            {
                throw new InvalidDataException(
                    $"Authenticode doğrulaması başarısız oldu (0x{result:X8}).");
            }
        }
        finally
        {
            if (fileInfoPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(fileInfoPointer);
            }

            if (filePathPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(filePathPointer);
            }
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(
        IntPtr windowHandle,
        [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
        ref WinTrustData trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public WinTrustUiChoice UiChoice;
        public WinTrustRevocationChecks RevocationChecks;
        public WinTrustUnionChoice UnionChoice;
        public IntPtr FileInfo;
        public WinTrustStateAction StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public WinTrustProviderFlags ProviderFlags;
        public WinTrustUiContext UiContext;
    }

    private enum WinTrustUiChoice : uint
    {
        None = 2
    }

    private enum WinTrustRevocationChecks : uint
    {
        WholeChain = 1
    }

    private enum WinTrustUnionChoice : uint
    {
        File = 1
    }

    private enum WinTrustStateAction : uint
    {
        Ignore = 0
    }

    [Flags]
    private enum WinTrustProviderFlags : uint
    {
        RevocationCheckChainExcludeRoot = 0x00000080
    }

    private enum WinTrustUiContext : uint
    {
        Execute = 0
    }
}
