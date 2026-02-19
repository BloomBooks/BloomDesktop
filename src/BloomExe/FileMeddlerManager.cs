using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Bloom
{
    public class FileMeddlerManager
    {
        public static bool IsMeddling
        {
            get { return _meddlers.Count > 0; }
        }

        static Dictionary<string, Process> _meddlers = new Dictionary<string, Process>();

        public static void Start(string folderPath)
        {
            if (Directory.Exists(folderPath) && !_meddlers.ContainsKey(folderPath))
            {
                var meddle = Path.Combine(BloomFileLocator.GetCodeBaseFolder(), "meddle.exe");
                if (SIL.IO.RobustFile.Exists(meddle))
                {
                    Debug.WriteLine($"Starting meddle.exe in {folderPath}");
                    var proc = Process.Start(
                        new ProcessStartInfo()
                        {
                            FileName = meddle,
                            WorkingDirectory = folderPath,
                            //UseShellExecute = false,	// causes hang opening a Team Collection for some reason
                            ErrorDialog = false,
                        }
                    );
                    _meddlers[folderPath] = proc;
                }
            }
        }

        public static void Stop()
        {
            Debug.WriteLine($"Stopping all instances of meddle.exe");
            foreach (var meddler in _meddlers.Values)
            {
                if (meddler != null && !meddler.HasExited)
                    meddler.Kill();
            }
            _meddlers.Clear();
        }
    }
}
