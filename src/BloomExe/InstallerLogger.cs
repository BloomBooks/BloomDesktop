using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;
using SIL.IO;
using Squirrel.SimpleSplat;

namespace Bloom
{
    // This class is adapted from one found in the Squirrel source code.
    public class InstallerLogger : ILogger, IDisposable
    {
        TextWriter inner;
        readonly object gate = 42;
        public LogLevel Level { get; set; }

        public InstallerLogger(bool saveInTemp, string commandSuffix = null)
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    var dir = saveInTemp
                        ? Path.GetTempPath()
                        : Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                    var fileName =
                        commandSuffix == null
                            ? $"Squirrel.{i}.log"
                            : $"Squirrel-{commandSuffix}.{i}.log";
                    var file = Path.Combine(dir, fileName.Replace(".0.log", ".log"));
                    var str = RobustFile.Open(
                        file,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.Read
                    );
                    inner = new StreamWriter(str, Encoding.UTF8, 4096, false) { AutoFlush = true };
                    return;
                }
                catch (Exception ex)
                {
                    // opening file didn't work? Keep trying
                    Console.Error.WriteLine(
                        "Couldn't open log file, trying new file: " + ex.ToString()
                    );
                }
            }
            // last ditch effort to allow logging to console if nothing else works
            inner = Console.Error;
        }

        public void Write([Localizable(false)] string message, LogLevel logLevel)
        {
            if (logLevel < Level)
            {
                return;
            }

            lock (gate)
                inner.WriteLine(
                    $"[{DateTime.Now.ToString("dd/MM/yy HH:mm:ss")}] {logLevel.ToString().ToLower()}: {message}"
                );
        }

        public void Dispose()
        {
            lock (gate)
            {
                inner.Flush();
                inner.Dispose();
            }
        }
    }
}
