using System.Diagnostics;
using System.Text;

namespace BloomFreezeDoctor;

/// <summary>
/// The Doctor's own log.
///
/// A tool whose job is to explain other programs' failures has no business being unexplainable itself. This
/// exists because the first time the Doctor did something unexpected, there was nothing to read: its
/// internal notes went to <c>Debug.WriteLine</c>, which nobody sees in a released build, so the only way to
/// find out what it had decided was to guess and re-run.
///
/// Deliberately primitive: append a line, keep it small, never throw, no dependencies. A logger that can
/// fail or block would be a poor addition to a program that must keep working while everything around it
/// does not.
/// </summary>
public static class DoctorLog
{
    private static readonly object _lock = new();
    private static string? _path;

    /// <summary>Rotates at this size, keeping one previous file. Enough for days of ordinary operation.</summary>
    private const long MaxBytes = 2 * 1024 * 1024;

    /// <summary>Where the log lives. Beside the outbox, so support asks for one folder rather than two.</summary>
    public static string Path =>
        _path ??= System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SIL",
            "BloomFreezeDoctor",
            "doctor.log"
        );

    /// <summary>Writes one line, with a timestamp. Never throws.</summary>
    public static void Write(string message)
    {
        Debug.WriteLine("[FreezeDoctor] " + message);
        try
        {
            lock (_lock)
            {
                var directory = System.IO.Path.GetDirectoryName(Path)!;
                Directory.CreateDirectory(directory);
                RotateIfBig();
                File.AppendAllText(
                    Path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{Environment.ProcessId}] {message}{Environment.NewLine}",
                    Encoding.UTF8
                );
            }
        }
        catch (Exception)
        {
            // A log we cannot write is not a reason to stop watching.
        }
    }

    /// <summary>Writes a line about something that went wrong, including the exception's type and message.</summary>
    public static void Write(string message, Exception e) =>
        Write($"{message}: {e.GetType().Name}: {e.Message}");

    private static void RotateIfBig()
    {
        try
        {
            var info = new FileInfo(Path);
            if (!info.Exists || info.Length < MaxBytes)
                return;
            var previous = Path + ".1";
            if (File.Exists(previous))
                File.Delete(previous);
            File.Move(Path, previous);
        }
        catch (Exception)
        {
            // If rotation fails we would rather keep appending to a large file than lose the log.
        }
    }
}
