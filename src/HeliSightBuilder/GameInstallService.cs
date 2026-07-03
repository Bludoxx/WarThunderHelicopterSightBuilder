using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace HeliSightBuilder.Native;

public static partial class GameInstallService
{
    private static readonly string StateRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HeliSightBuilder", "installed-sights");

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex SteamLibraryRegex();

    public static string? DetectContentDirectory()
    {
        foreach (var candidate in CandidateContentDirectories())
        {
            if (IsWarThunderContentDirectory(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }

    public static bool IsWarThunderContentDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
        var full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)),
                "content", StringComparison.OrdinalIgnoreCase))
            return false;
        var gameRoot = Directory.GetParent(full)?.FullName;
        if (gameRoot is null) return false;
        return File.Exists(Path.Combine(gameRoot, "win64", "aces.exe")) ||
            File.Exists(Path.Combine(gameRoot, "aces.exe")) ||
            string.Equals(Path.GetFileName(gameRoot), "War Thunder", StringComparison.OrdinalIgnoreCase);
    }

    public static void Install(string builtFilesDirectory, string contentDirectory)
    {
        ValidateBuiltFiles(builtFilesDirectory);
        var content = ValidateContentDirectory(contentDirectory);
        var stateDirectory = StateDirectory(content);
        var recordPath = Path.Combine(stateDirectory, "install.json");

        if (!File.Exists(recordPath))
        {
            Directory.CreateDirectory(stateDirectory);
            BackupExisting(content, stateDirectory);
            File.WriteAllText(recordPath,
                JsonSerializer.Serialize(new InstallRecord(content, DateTimeOffset.UtcNow)));
        }

        var targetPackage = Path.Combine(content, "pkg_user");
        if (Directory.Exists(targetPackage)) Directory.Delete(targetPackage, true);
        CopyDirectory(Path.Combine(builtFilesDirectory, "pkg_user"), targetPackage);
        File.Copy(Path.Combine(builtFilesDirectory, "pkg_user.rq2"),
            Path.Combine(content, "pkg_user.rq2"), true);
        File.Copy(Path.Combine(builtFilesDirectory, "pkg_user.ver"),
            Path.Combine(content, "pkg_user.ver"), true);
    }

    public static bool Restore(string contentDirectory)
    {
        var content = ValidateContentDirectory(contentDirectory);
        var stateDirectory = StateDirectory(content);
        var recordPath = Path.Combine(stateDirectory, "install.json");
        if (!File.Exists(recordPath)) return false;

        RemoveInstalledFiles(content);
        var backup = Path.Combine(stateDirectory, "backup");
        if (Directory.Exists(backup))
        {
            var packageBackup = Path.Combine(backup, "pkg_user");
            if (Directory.Exists(packageBackup))
                CopyDirectory(packageBackup, Path.Combine(content, "pkg_user"));
            RestoreFile(backup, content, "pkg_user.rq2");
            RestoreFile(backup, content, "pkg_user.ver");
        }

        Directory.Delete(stateDirectory, true);
        return true;
    }

    public static void RemoveUntracked(string contentDirectory)
    {
        var content = ValidateContentDirectory(contentDirectory);
        RemoveInstalledFiles(content);
    }

    private static IEnumerable<string> CandidateContentDirectories()
    {
        var gameRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in SteamRoots())
            gameRoots.Add(Path.Combine(root, "steamapps", "common", "War Thunder"));

        gameRoots.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam", "steamapps", "common", "War Thunder"));
        gameRoots.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Steam", "steamapps", "common", "War Thunder"));
        gameRoots.Add(@"C:\WarThunder");
        gameRoots.Add(@"C:\Games\WarThunder");

        foreach (var root in gameRoots)
            yield return Path.Combine(root, "content");
    }

    private static IEnumerable<string> SteamRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string steamPath)
                roots.Add(steamPath.Replace('/', Path.DirectorySeparatorChar));
        }
        catch
        {
            // Fall back to standard Steam locations.
        }

        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));

        foreach (var root in roots.ToArray())
        {
            var libraries = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraries)) continue;
            try
            {
                foreach (Match match in SteamLibraryRegex().Matches(File.ReadAllText(libraries)))
                    roots.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
            }
            catch
            {
                // One unreadable library file must not block other candidates.
            }
        }
        return roots;
    }

    private static void BackupExisting(string content, string stateDirectory)
    {
        var backup = Path.Combine(stateDirectory, "backup");
        if (Directory.Exists(backup)) Directory.Delete(backup, true);
        Directory.CreateDirectory(backup);

        var package = Path.Combine(content, "pkg_user");
        if (Directory.Exists(package))
            CopyDirectory(package, Path.Combine(backup, "pkg_user"));
        BackupFile(content, backup, "pkg_user.rq2");
        BackupFile(content, backup, "pkg_user.ver");
    }

    private static void BackupFile(string sourceDirectory, string backupDirectory, string name)
    {
        var source = Path.Combine(sourceDirectory, name);
        if (File.Exists(source)) File.Copy(source, Path.Combine(backupDirectory, name), true);
    }

    private static void RestoreFile(string backupDirectory, string targetDirectory, string name)
    {
        var source = Path.Combine(backupDirectory, name);
        if (File.Exists(source)) File.Copy(source, Path.Combine(targetDirectory, name), true);
    }

    private static void RemoveInstalledFiles(string content)
    {
        var package = Path.Combine(content, "pkg_user");
        if (Directory.Exists(package)) Directory.Delete(package, true);
        foreach (var name in new[] { "pkg_user.rq2", "pkg_user.ver" })
        {
            var path = Path.Combine(content, name);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string ValidateContentDirectory(string contentDirectory)
    {
        if (!IsWarThunderContentDirectory(contentDirectory))
            throw new DirectoryNotFoundException(
                "Select War Thunder's content folder, for example: Steam\\steamapps\\common\\War Thunder\\content");
        return Path.GetFullPath(contentDirectory);
    }

    private static void ValidateBuiltFiles(string directory)
    {
        if (!File.Exists(Path.Combine(directory, "pkg_user", "base.vromfs.bin")) ||
            !File.Exists(Path.Combine(directory, "pkg_user.rq2")) ||
            !File.Exists(Path.Combine(directory, "pkg_user.ver")))
            throw new InvalidDataException("The generated install files are incomplete.");
    }

    private static string StateDirectory(string contentDirectory)
    {
        var hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(contentDirectory.ToUpperInvariant())));
        return Path.Combine(StateRoot, hash[..16]);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    private sealed record InstallRecord(string ContentDirectory, DateTimeOffset InstalledAt);
}
