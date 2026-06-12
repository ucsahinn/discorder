using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Discorder.Core.Security;

namespace Discorder.Core.Updates;

public static class UpdatePackageValidator
{
    public const string ManifestFileName = "discorder.update-manifest.json";
    public const long MaxPackageBytes = 256L * 1024L * 1024L;
    public const long MaxExtractedBytes = 700L * 1024L * 1024L;
    public const int MaxEntryCount = 5000;

    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static IReadOnlyList<UpdatePackageFile> ValidateArchive(
        string packagePath,
        string executableName,
        string? expectedVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);

        var packageInfo = new FileInfo(packagePath);
        if (!packageInfo.Exists)
        {
            throw new FileNotFoundException(
                "Güncelleme paketi bulunamadı.",
                packagePath);
        }

        if (packageInfo.Length <= 0 || packageInfo.Length > MaxPackageBytes)
        {
            throw new InvalidDataException(
                "Güncelleme paketi beklenen boyut aralığında değil.");
        }

        using var archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaxEntryCount)
        {
            throw new InvalidDataException(
                "Güncelleme paketi beklenen dosya sayısında değil.");
        }

        var totalSize = 0L;
        var files = new List<UpdatePackageFile>();
        var filePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasExecutable = false;
        ZipArchiveEntry? manifestEntry = null;

        foreach (var entry in archive.Entries)
        {
            var relativePath = NormalizeEntryPath(entry.FullName);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            if (IsDirectoryEntry(entry, relativePath))
            {
                continue;
            }

            if (!filePaths.Add(relativePath))
            {
                throw new InvalidDataException(
                    $"Güncelleme paketi aynı dosyayı birden fazla içeriyor: {relativePath}");
            }

            totalSize += entry.Length;
            if (totalSize > MaxExtractedBytes)
            {
                throw new InvalidDataException(
                    "Güncelleme paketi çıkarıldığında beklenenden büyük.");
            }

            if (string.Equals(
                    relativePath,
                    executableName,
                    StringComparison.OrdinalIgnoreCase))
            {
                hasExecutable = true;
            }

            files.Add(new UpdatePackageFile(relativePath, entry.Length));
            if (string.Equals(
                    relativePath,
                    ManifestFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                manifestEntry = entry;
            }
        }

        if (!hasExecutable)
        {
            throw new InvalidDataException(
                $"Güncelleme paketi {executableName} dosyasını içermiyor.");
        }

        if (manifestEntry is null)
        {
            throw new InvalidDataException(
                "Güncelleme paketi doğrulama manifestini içermiyor.");
        }

        var manifest = ReadManifest(manifestEntry);
        ValidateManifest(manifest, executableName, expectedVersion);
        var manifestPaths = manifest.Files
            .Select(file => NormalizeRelativePath(file.Path))
            .Append(ManifestFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!manifestPaths.Contains(file.Path))
            {
                throw new InvalidDataException(
                    $"Güncelleme paketi manifest dışı dosya içeriyor: {file.Path}");
            }
        }

        return files;
    }

    public static void ExtractToDirectory(
        string packagePath,
        string destinationDirectory,
        string executableName,
        string? expectedVersion = null,
        string? expectedSignerThumbprint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ValidateArchive(packagePath, executableName, expectedVersion);
        Directory.CreateDirectory(destinationDirectory);
        ZipFile.ExtractToDirectory(packagePath, destinationDirectory);
        ValidatePayload(
            destinationDirectory,
            executableName,
            expectedVersion,
            expectedSignerThumbprint);
    }

    public static UpdateManifest ValidatePayload(
        string payloadDirectory,
        string executableName,
        string? expectedVersion = null,
        string? expectedSignerThumbprint = null,
        UpdateManifest? expectedManifest = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);

        var root = Path.GetFullPath(payloadDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(root);
        }

        var executablePath = GetSafePath(root, executableName);
        if (!File.Exists(executablePath))
        {
            throw new InvalidDataException(
                $"Güncelleme paketi {executableName} dosyasını içermiyor.");
        }

        var manifestPath = Path.Combine(root, ManifestFileName);
        if (expectedManifest is null && !File.Exists(manifestPath))
        {
            throw new InvalidDataException(
                "Güncelleme paketi doğrulama manifestini içermiyor.");
        }

        var manifest = expectedManifest ?? ReadManifest(manifestPath);
        ValidateManifest(manifest, executableName, expectedVersion);

        foreach (var file in manifest.Files)
        {
            ValidateManifestPath(file.Path);
            var path = GetSafePath(root, file.Path);
            if (!File.Exists(path))
            {
                throw new InvalidDataException(
                    $"Güncelleme manifestindeki dosya eksik: {file.Path}");
            }

            var info = new FileInfo(path);
            if (info.Length != file.Length)
            {
                throw new InvalidDataException(
                    $"Güncelleme dosya boyutu doğrulanamadı: {file.Path}");
            }

            var sha256 = ComputeSha256(path);
            if (!string.Equals(
                    sha256,
                    file.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Güncelleme dosya özeti doğrulanamadı: {file.Path}");
            }

            if (!string.IsNullOrWhiteSpace(expectedSignerThumbprint)
                && AuthenticodeSignatureVerifier.ShouldVerify(file.Path))
            {
                AuthenticodeSignatureVerifier.VerifyFile(
                    path,
                    expectedSignerThumbprint);
            }
        }

        return manifest;
    }

    public static UpdateManifest? TryReadManifest(string rootDirectory)
    {
        var manifestPath = Path.Combine(
            Path.GetFullPath(rootDirectory),
            ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        return ReadManifest(manifestPath);
    }

    public static void WriteManifest(string rootDirectory, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var root = Path.GetFullPath(rootDirectory);
        var files = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path))
            .Where(path => !string.Equals(
                path,
                ManifestFileName,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                ValidateManifestPath(path);
                var fullPath = GetSafePath(root, path);
                var info = new FileInfo(fullPath);
                return new UpdateManifestFile(
                    NormalizeRelativePath(path),
                    info.Length,
                    ComputeSha256(fullPath));
            })
            .ToArray();

        var manifest = new UpdateManifest(version, files);
        WriteManifest(root, manifest);
    }

    public static void WriteManifest(string rootDirectory, UpdateManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(manifest);

        var manifestPath = Path.Combine(
            Path.GetFullPath(rootDirectory),
            ManifestFileName);
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, JsonOptions));
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static string GetSafePath(string rootDirectory, string relativePath)
    {
        ValidateManifestPath(relativePath);

        var root = Path.GetFullPath(rootDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!IsWithinDirectory(root, fullPath))
        {
            throw new InvalidDataException(
                $"Güncelleme dosya yolu güvenli değil: {relativePath}");
        }

        return fullPath;
    }

    public static string NormalizeRelativePath(string relativePath)
    {
        return NormalizeEntryPath(relativePath);
    }

    private static UpdateManifest ReadManifest(string manifestPath)
    {
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(
            File.ReadAllText(manifestPath),
            JsonOptions);

        if (manifest is null)
        {
            throw new InvalidDataException(
                "Güncelleme manifesti okunamadı.");
        }

        return manifest;
    }

    private static UpdateManifest ReadManifest(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(
            stream,
            JsonOptions);

        if (manifest is null)
        {
            throw new InvalidDataException(
                "Güncelleme manifesti okunamadı.");
        }

        return manifest;
    }

    private static void ValidateManifest(
        UpdateManifest manifest,
        string executableName,
        string? expectedVersion)
    {
        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new InvalidDataException(
                "Güncelleme manifesti sürüm bilgisi içermiyor.");
        }

        if (!string.IsNullOrWhiteSpace(expectedVersion)
            && !string.Equals(
                manifest.Version,
                expectedVersion,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Güncelleme manifesti beklenen sürümle eşleşmiyor: {manifest.Version}");
        }

        if (manifest.Files.Count == 0)
        {
            throw new InvalidDataException(
                "Güncelleme manifesti dosya listesi içermiyor.");
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            ValidateManifestPath(file.Path);
            if (!paths.Add(NormalizeRelativePath(file.Path)))
            {
                throw new InvalidDataException(
                    $"Güncelleme manifesti aynı dosyayı birden fazla içeriyor: {file.Path}");
            }

            if (file.Length < 0
                || string.IsNullOrWhiteSpace(file.Sha256)
                || file.Sha256.Length != 64
                || !file.Sha256.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException(
                    $"Güncelleme manifesti geçersiz dosya bilgisi içeriyor: {file.Path}");
            }
        }

        if (!manifest.Files.Any(file => string.Equals(
                NormalizeRelativePath(file.Path),
                NormalizeRelativePath(executableName),
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"Güncelleme manifesti {executableName} dosyasını içermiyor.");
        }
    }

    private static string NormalizeEntryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (path[0] == '/' || path[0] == '\\')
        {
            throw new InvalidDataException(
                $"Güncelleme dosya yolu mutlak olamaz: {path}");
        }

        var normalized = path.Replace('\\', '/');
        ValidateManifestPath(normalized);
        return normalized.Replace('/', Path.DirectorySeparatorChar);
    }

    private static void ValidateManifestPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException(
                "Güncelleme dosya yolu boş olamaz.");
        }

        if (Path.IsPathRooted(path) || Path.IsPathFullyQualified(path))
        {
            throw new InvalidDataException(
                $"Güncelleme dosya yolu mutlak olamaz: {path}");
        }

        var parts = path
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part is "." or ".."))
        {
            throw new InvalidDataException(
                $"Güncelleme dosya yolu güvenli değil: {path}");
        }
    }

    private static bool IsDirectoryEntry(ZipArchiveEntry entry, string path)
    {
        return entry.FullName.EndsWith('/')
            || entry.FullName.EndsWith('\\')
            || string.IsNullOrEmpty(entry.Name)
            || path.EndsWith(Path.DirectorySeparatorChar);
    }

    private static bool IsWithinDirectory(string rootDirectory, string path)
    {
        var root = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record UpdatePackageFile(string Path, long Length);

public sealed record UpdateManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("files")] IReadOnlyList<UpdateManifestFile> Files);

public sealed record UpdateManifestFile(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("length")] long Length,
    [property: JsonPropertyName("sha256")] string Sha256);
