using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;

namespace MarsXZMedia;

internal static class InstallAccessHelper
{
    public static bool IsRunningAsAdministrator()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return false;
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool NeedsElevationForInstall(IEnumerable<string> targetPaths, out string reason)
        => NeedsElevationForWrite(targetPaths, out reason, "изменения файлов рядом с программой");

    public static bool NeedsElevationForWrite(IEnumerable<string> targetPaths, out string reason, string operationDescription)
    {
        var normalizedTargets = targetPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedTargets.Count == 0)
        {
            reason = string.Empty;
            return false;
        }

        string directory = Path.GetDirectoryName(normalizedTargets[0]) ?? AppPaths.AppDirectory;
        if (CanWriteToDirectory(directory, out var error))
        {
            reason = string.Empty;
            return false;
        }

        var details = new List<string>();
        if (IsKnownProtectedDirectory(directory))
            details.Add("папка относится к защищённым системным каталогам");
        if (IsDirectoryReadOnly(directory))
            details.Add("папка помечена как только для чтения");

        if (error is UnauthorizedAccessException)
            details.Add("Windows запретил запись без повышенных прав");
        else if (error is IOException ioEx && !string.IsNullOrWhiteSpace(ioEx.Message))
            details.Add(ioEx.Message);
        else if (error != null && !string.IsNullOrWhiteSpace(error.Message))
            details.Add(error.Message);

        if (details.Count == 0)
            details.Add("рядом с программой нельзя создать служебный файл");

        string fileNames = string.Join(", ", normalizedTargets.Select(Path.GetFileName).Where(x => !string.IsNullOrWhiteSpace(x)));
        reason = $"Для {operationDescription} нужен доступ к папке рядом с программой. Файлы: {fileNames}. {string.Join("; ", details)}.";
        return true;
    }

    public static bool TryRestartElevatedForSetup(out Exception? error)
    {
        error = null;
        try
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exe))
                throw new InvalidOperationException("Не удалось определить путь к исполняемому файлу.");

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--setup",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppPaths.AppDirectory
            };

            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    private static bool CanWriteToDirectory(string directory, out Exception? error)
    {
        error = null;
        try
        {
            Directory.CreateDirectory(directory);
            string testFile = Path.Combine(directory, $".marsxz_write_test_{Guid.NewGuid():N}.tmp");
            using (new FileStream(testFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }
            File.Delete(testFile);
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    private static bool IsDirectoryReadOnly(string directory)
    {
        try
        {
            return Directory.Exists(directory) && new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReadOnly);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsKnownProtectedDirectory(string directory)
    {
        try
        {
            string fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var protectedRoots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var root in protectedRoots)
            {
                if (fullDirectory.Equals(root, StringComparison.OrdinalIgnoreCase))
                    return true;

                string prefix = root + Path.DirectorySeparatorChar;
                if (fullDirectory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
        }

        return false;
    }
}
