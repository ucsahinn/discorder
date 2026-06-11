using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Discorder.Core.WireSock;

namespace Discorder.App.Security;

public sealed class WireSockPackageVerifier : IWireSockPackageVerifier
{
    private static readonly Guid GenericVerifyV2 = new(
        "00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public void VerifyInstaller(string installerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Authenticode doğrulaması Windows gerektirir.");
        }

        if (!File.Exists(installerPath))
        {
            throw new FileNotFoundException(
                "WireSock kurucusu bulunamadı.",
                installerPath);
        }

        VerifyAuthenticode(installerPath);
        VerifyPublisher(installerPath);
        VerifyVersionInfo(installerPath);
    }

    public void VerifyClient(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        if (!IsKnownCliName(Path.GetFileName(executablePath)))
        {
            throw new InvalidDataException(
                "Beklenmeyen WireSock istemci dosya adı.");
        }

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "WireSock istemcisi bulunamadı.",
                executablePath);
        }

        VerifyAuthenticode(executablePath);
        VerifyPublisher(executablePath);
    }

    private static bool IsKnownCliName(string fileName)
    {
        return fileName.Equals(
                WireSockPackage.CliExecutableFileName,
                StringComparison.OrdinalIgnoreCase);
    }

    private static void VerifyAuthenticode(string installerPath)
    {
        var filePathPointer = IntPtr.Zero;
        var fileInfoPointer = IntPtr.Zero;

        try
        {
            filePathPointer = Marshal.StringToCoTaskMemUni(installerPath);
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
                    $"WireSock Authenticode doğrulaması başarısız oldu " +
                    $"(0x{result:X8}).");
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

    private static void VerifyPublisher(string installerPath)
    {
#pragma warning disable SYSLIB0057
        using var certificate = new X509Certificate2(
            X509Certificate.CreateFromSignedFile(installerPath));
#pragma warning restore SYSLIB0057

        var publisher = certificate.GetNameInfo(
            X509NameType.SimpleName,
            forIssuer: false);

        if (!publisher.Equals(
                WireSockPackage.ExpectedPublisher,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Beklenmeyen WireSock yayıncısı: {publisher}.");
        }
    }

    private static void VerifyVersionInfo(string installerPath)
    {
        if (Path.GetExtension(installerPath).Equals(
                ".msi",
                StringComparison.OrdinalIgnoreCase))
        {
            VerifyMsiVersionInfo(installerPath);
            return;
        }

        var versionInfo = FileVersionInfo.GetVersionInfo(installerPath);

        if (!string.Equals(
                versionInfo.ProductName,
                WireSockPackage.ExpectedProductName,
                StringComparison.Ordinal)
            || !string.Equals(
                versionInfo.ProductVersion,
                WireSockPackage.Version,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "WireSock kurucu ürün veya sürüm bilgisi beklenen değerle eşleşmiyor.");
        }
    }

    private static void VerifyMsiVersionInfo(string installerPath)
    {
        var productName = ReadMsiProperty(installerPath, "ProductName");
        var productVersion = ReadMsiProperty(installerPath, "ProductVersion");

        if (!string.Equals(
                productName,
                WireSockPackage.ExpectedProductName,
                StringComparison.Ordinal)
            || !string.Equals(
                productVersion,
                WireSockPackage.Version,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "WireSock MSI ürün veya sürüm bilgisi beklenen değerle eşleşmiyor.");
        }
    }

    private static string? ReadMsiProperty(string installerPath, string propertyName)
    {
        var databaseHandle = IntPtr.Zero;
        var viewHandle = IntPtr.Zero;
        var recordHandle = IntPtr.Zero;

        try
        {
            var result = MsiOpenDatabase(
                installerPath,
                IntPtr.Zero,
                out databaseHandle);
            ThrowIfMsiFailed(result, "MSI veritabanı açılamadı.");

            result = MsiDatabaseOpenView(
                databaseHandle,
                $"SELECT `Value` FROM `Property` WHERE `Property`='{propertyName}'",
                out viewHandle);
            ThrowIfMsiFailed(result, "MSI özellik görünümü açılamadı.");

            result = MsiViewExecute(viewHandle, IntPtr.Zero);
            ThrowIfMsiFailed(result, "MSI özellik sorgusu çalıştırılamadı.");

            result = MsiViewFetch(viewHandle, out recordHandle);
            if (result == MsiNoMoreItems)
            {
                return null;
            }

            ThrowIfMsiFailed(result, "MSI özellik kaydı okunamadı.");

            var valueLength = 256u;
            var value = new char[valueLength + 1];
            result = MsiRecordGetString(recordHandle, 1, value, ref valueLength);
            if (result == MsiMoreData)
            {
                value = new char[valueLength + 1];
                valueLength = (uint)value.Length;
                result = MsiRecordGetString(recordHandle, 1, value, ref valueLength);
            }

            ThrowIfMsiFailed(result, "MSI özellik değeri okunamadı.");
            return new string(value, 0, (int)valueLength);
        }
        finally
        {
            CloseMsiHandle(recordHandle);
            CloseMsiHandle(viewHandle);
            CloseMsiHandle(databaseHandle);
        }
    }

    private static void ThrowIfMsiFailed(uint result, string message)
    {
        if (result != MsiSuccess)
        {
            throw new InvalidDataException($"{message} Hata kodu: {result}.");
        }
    }

    private static void CloseMsiHandle(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            _ = MsiCloseHandle(handle);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(
        IntPtr windowHandle,
        [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
        ref WinTrustData trustData);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiOpenDatabase(
        string databasePath,
        IntPtr persist,
        out IntPtr databaseHandle);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiDatabaseOpenView(
        IntPtr databaseHandle,
        string query,
        out IntPtr viewHandle);

    [DllImport("msi.dll")]
    private static extern uint MsiViewExecute(
        IntPtr viewHandle,
        IntPtr recordHandle);

    [DllImport("msi.dll")]
    private static extern uint MsiViewFetch(
        IntPtr viewHandle,
        out IntPtr recordHandle);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiRecordGetString(
        IntPtr recordHandle,
        uint field,
        [Out] char[] valueBuffer,
        ref uint valueBufferLength);

    [DllImport("msi.dll")]
    private static extern uint MsiCloseHandle(IntPtr handle);

    private const uint MsiSuccess = 0;
    private const uint MsiMoreData = 234;
    private const uint MsiNoMoreItems = 259;

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
