using System;
using System.IO;

namespace Bloom.TeamCollection
{
	internal class FileSystemEventRecord
	{
		public FileSystemEventArgs  EventArgs { get; set; }
		public DateTime Timestamp { get; set; }

		public FileSystemEventRecord(FileSystemEventArgs eventArgs, DateTime? timestamp = null)
		{
			EventArgs = eventArgs;
			Timestamp = timestamp ?? DateTime.Now;
		}
	}
}
