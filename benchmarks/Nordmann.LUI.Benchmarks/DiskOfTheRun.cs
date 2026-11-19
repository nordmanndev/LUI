namespace Nordmann.LUI.Benchmarks;

/// <summary>
/// The disk the databases of the benchmarks live on (issue #178). A write waits for the disk: the write benchmark on the
/// network file system of a workplace measured 1.85 ms where the local disk measured 0.225 ms. A number of a write without
/// the disk it was measured on is a rumour — the report names it, and says whether it is a network file system.
/// </summary>
internal static class DiskOfTheRun
{
    /// <summary>The file systems that are on the other side of a network.</summary>
    private static readonly string[] NetworkFileSystems =
    [
        "nfs", "nfs3", "nfs4", "cifs", "smb", "smb2", "smb3", "smbfs", "9p", "v9fs", "afs", "ceph", "glusterfs", "lustre", "fuse.sshfs", "fuse.glusterfs", "davfs"
    ];

    /// <summary>The disk of the directory the databases of the run are created in.</summary>
    /// <returns>The file system, whether it is a local one or a network one, and the directory.</returns>
    internal static string Describe() => Describe(Path.GetFullPath(Path.GetTempPath()));

    /// <summary>The disk of a directory.</summary>
    /// <param name="directory">The directory.</param>
    /// <returns>The file system, whether it is a local one or a network one, and the directory.</returns>
    internal static string Describe(string directory)
    {
        var (fileSystem, isNetwork) = OperatingSystem.IsLinux()
            ? FromMounts(directory)
            : FromDrive(directory);

        // a disk that could not be identified is not a local one: the report says so, and the test does not take it for local
        var kind = string.Equals(fileSystem, "unknown", StringComparison.Ordinal) ? "unidentified" : isNetwork ? "network" : "local";
        return $"{fileSystem}, {kind} — {directory.TrimEnd(Path.DirectorySeparatorChar)}";
    }

    /// <summary>Linux: the file system of the longest mount point that is a prefix of the directory.</summary>
    private static (string FileSystem, bool IsNetwork) FromMounts(string directory)
    {
        try
        {
            var best = (Point: string.Empty, Type: "unknown");
            foreach (var line in File.ReadLines("/proc/mounts"))
            {
                var parts = line.Split(' ');
                if (parts.Length < 3)
                {
                    continue;
                }

                var point = parts[1].Replace("\\040", " ", StringComparison.Ordinal);
                var isPrefix = string.Equals(point, "/", StringComparison.Ordinal)
                    || string.Equals(directory.TrimEnd('/'), point, StringComparison.Ordinal)
                    || directory.StartsWith(point + "/", StringComparison.Ordinal);
                if (isPrefix && point.Length >= best.Point.Length)
                {
                    best = (point, parts[2]);
                }
            }

            return (best.Type, NetworkFileSystems.Contains(best.Type, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ("unknown", false);
        }
    }

    /// <summary>Windows and macOS: the drive the directory is on.</summary>
    private static (string FileSystem, bool IsNetwork) FromDrive(string directory)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(directory) ?? directory);
            return (drive.DriveFormat, drive.DriveType == DriveType.Network);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return ("unknown", false);
        }
    }
}
