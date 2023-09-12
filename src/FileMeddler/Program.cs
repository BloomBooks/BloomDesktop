using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace FileMeddler
{
	class Program
	{
		private const int kRetryMilliseconds = 50;
		private const int kLockMilliseconds = 200;
		private const int kGiveUpMilliseconds = 5000;

		private static readonly HashSet<string> s_extensionsToIgnore = new HashSet<string> { ".exe", ".dll", ".ini", ".pdb" };

		//		private static ConcurrentDictionary<string,int> _lockedFiles = new ConcurrentDictionary<string, byte>();
		private static readonly HashSet<string> s_filesInProcess = new HashSet<string>();
		private static string s_root = "";

		private static void Main(string[] args)
		{
			var consoleColor = Console.ForegroundColor;

			var filter = "*.*";
			if (args.Length == 1)
				filter = args[0];

			s_root = Directory.GetCurrentDirectory();

			var watcher = new FileSystemWatcher()
			{
				Path = s_root,
				Filter = filter
			};
			watcher.Created += SomethingHappened;
			watcher.Changed += SomethingHappened;
			watcher.Renamed += SomethingHappened;
			watcher.Deleted += SomethingHappened;
			watcher.IncludeSubdirectories = true;

			watcher.EnableRaisingEvents = true;

			Console.WriteLine("Ready to meddle. Press Enter to stop.");
			Console.ReadLine();
			Console.ForegroundColor = consoleColor; // return text to the original color
		}


		private static void SomethingHappened(object sender, FileSystemEventArgs e)
		{
			// if a new directory was created or renamed, camp on all of its files
			if (Directory.Exists(e.FullPath))
			{
				try
				{
					foreach (var f in Directory.GetFiles(e.FullPath))
					{
						var x = new Thread(() => CampOnFile(f, e.ChangeType));
						x.Start();
					}
				}
				catch (Exception err)
				{
					Print(ConsoleColor.Red, "   Error trying to camp on all files in: " + e.FullPath);
					Print(ConsoleColor.Red, "   " + err.Message);
				}
				return;
			}

			//the FileWatcher won't give us a new one until
			//we return. Since timing is the whole point here,
			//we spawn a thread to try and grab that file and return quickly.
			var t = new Thread(() => CampOnFile(e.FullPath, e.ChangeType));
			t.Start();
		}

		private static void CampOnFile(string fullPath, WatcherChangeTypes changeType)
		{
			var filename = Path.GetFileName(fullPath);

			var extension = Path.GetExtension(filename);
			if (s_extensionsToIgnore.Contains(extension.ToLowerInvariant()))
				return;

			if (s_filesInProcess.Contains(fullPath))
			{
				//Print(ConsoleColor.Gray, "   Already processing: " + filename);
				return;
			}
			else
			{
				s_filesInProcess.Add(fullPath);
			}

			var startTime = DateTime.Now.AddMilliseconds(kGiveUpMilliseconds);
			var reportedWaiting = false;
			var relativePath = fullPath.Replace(s_root, "") + " ";
			switch (changeType)
			{
				case WatcherChangeTypes.Created:
					Print(ConsoleColor.DarkMagenta, "Creation: " + relativePath);
					break;
				case WatcherChangeTypes.Deleted:
					Print(ConsoleColor.DarkRed, "Deletion: " + relativePath);
					s_filesInProcess.Remove(fullPath);
					return;
				case WatcherChangeTypes.Changed:
					Print(ConsoleColor.Cyan, "Modified: " + relativePath);
					break;
				case WatcherChangeTypes.Renamed:
					Print(ConsoleColor.DarkCyan, "Renamed: " + relativePath);
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
			do
			{
				try
				{
					using(File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.None))
					{
						Print(ConsoleColor.Yellow, "   Locking: " + filename);
						Thread.Sleep(kLockMilliseconds);
					}
					Print(ConsoleColor.Green, "   Released: " + filename);
					s_filesInProcess.Remove(fullPath);
					return;
				}
				catch(FileNotFoundException)
				{
					Print(ConsoleColor.DarkGreen, "   File gone: " + filename);
					s_filesInProcess.Remove(fullPath);
					return;
				}
				catch(Exception error)
				{
					if(DateTime.Now > startTime)
					{
						Print(ConsoleColor.Red, "   Giving up waiting for: " + filename);
						Print(ConsoleColor.Red, error.Message);
						s_filesInProcess.Remove(fullPath);
						return;
					}
					if(!reportedWaiting)
					{
						Print(ConsoleColor.Magenta, "   Waiting to acquire: " + filename);
					}
					reportedWaiting = true;
					Thread.Sleep(kRetryMilliseconds);
				}
			} while(true);
		}

		private static void Print(ConsoleColor color, string message)
		{
			lock(Console.Out)
			{
				Console.ForegroundColor = color;
				Console.WriteLine(message);
			}
		}
	}
}
