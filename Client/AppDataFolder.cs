using System.IO;

namespace AionDPS;

/// <summary>
/// Where this build keeps its settings, fight history, sessions and saved character:
/// %APPDATA%\Aion DPS Meter (Stacy). SkeeveAN's own meter uses %APPDATA%\Aion DPS Meter, and with
/// both installed they rewrote each other's settings file (each keeps only the keys it knows).
/// On first use, whatever the shared folder holds is copied over - copied, not moved, so the
/// other meter keeps its own.
/// </summary>
public static class AppDataFolder
{
    private const string FolderName = "Aion DPS Meter (Stacy)";
    private const string SharedFolderName = "Aion DPS Meter";
    private static readonly object Gate = new();
    private static string? _path;

    public static string Path
    {
        get
        {
            lock (Gate)
            {
                return _path ??= Prepare();
            }
        }
    }

    private static string Prepare()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string folder = System.IO.Path.Combine(root, FolderName);
        string shared = System.IO.Path.Combine(root, SharedFolderName);
        if (!Directory.Exists(folder) && Directory.Exists(shared))
        {
            try
            {
                CopyDirectory(shared, folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file in use by the other meter: start with what was copied, defaults for the rest.
            }
        }

        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
        {
            File.Copy(file, System.IO.Path.Combine(to, System.IO.Path.GetFileName(file)), overwrite: false);
        }

        foreach (string directory in Directory.GetDirectories(from))
        {
            CopyDirectory(directory, System.IO.Path.Combine(to, System.IO.Path.GetFileName(directory)));
        }
    }
}
