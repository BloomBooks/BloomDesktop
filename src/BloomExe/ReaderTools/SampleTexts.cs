
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Bloom.Collection;
using BloomTemp;

namespace Bloom.ReaderTools
{
	class SampleTexts : IDisposable
	{
		private FileSystemWatcher _sampleTextsWatcher;
		private bool _sampleTextsChanged = true;
		private readonly CollectionSettings _currentCollectionSettings;
		private TemporaryFolder _tempDir;
		private bool _disposed;
		private readonly string[] _readableFileTypes = {".txt", ".js"};

		public SampleTexts(CollectionSettings currentCollectionSettings, Action callWhenInitialized = null)
		{
			_currentCollectionSettings = currentCollectionSettings;

			// set up the cache directory
			_tempDir = new TemporaryFolder("BloomSampleTextsCache");

			// initialize the file cache
			var path = Path.Combine(Path.GetDirectoryName(_currentCollectionSettings.SettingsFilePath), "Sample Texts");
			Initialize(path);

			// let the caller know the cache is ready
			if (callWhenInitialized != null)
				callWhenInitialized();

			// start the file watcher
			_sampleTextsWatcher = new FileSystemWatcher { Path = path };
			_sampleTextsWatcher.Created += SampleTextsOnChange;
			_sampleTextsWatcher.Changed += SampleTextsOnChange;
			_sampleTextsWatcher.Deleted += SampleTextsOnChange;
			_sampleTextsWatcher.EnableRaisingEvents = true;
		}

		private void Initialize(string sourceDirName)
		{
			foreach (var fileName in Directory.GetFiles(sourceDirName))
			{
				var info = new FileInfo(fileName);
				var destination = _tempDir.Combine(info.Name);


				if (info.Extension.ToLower() == ".js")
				{
					File.Copy(fileName, destination);
				}
				else
				{
					var contents = string.Empty;

					if (_readableFileTypes.Any(t => info.Extension.ToLower() == t))
						contents = ProcessFile(fileName);

					File.WriteAllText(destination, contents, Encoding.UTF8);
				}

			}
		}

		private string ProcessFile(string fileName)
		{
			// first try utf-8/ascii encoding (the .Net default)
			var text = File.ReadAllText(fileName);

			// If the "unknown" character (65533) is present, C# did not sucessfully decode the file. Try the system default encoding and codepage.
			if (text.Contains((char)65533))
				text = File.ReadAllText(fileName, Encoding.Default);

			// replace punctuation with a space
			const string pattern = "(^\\p{P}+)"                      // punctuation at the beginning of a string
								   + "|(\\p{P}+[\\s\\p{Z}]+\\p{P}+)" // punctuation within a sentence, between 2 words (word" "word)
								   + "|([\\s\\p{Z}]+\\p{P}+)"        // punctuation within a sentence, before a word
								   + "|(\\p{P}+[\\s\\p{Z}]+)"        // punctuation within a sentence, after a word
								   + "|(\\p{P}+$)";                  // punctuation at the end of a string

			var regex = new Regex(pattern);
			text = regex.Replace(text, " ").Trim().ToLower();

			// split into words using space characters
			regex = new Regex("[\\p{Z}]+");
			var words = regex.Split(text).ToArray();
			Array.Sort(words);

			return text;
		}

		private void SampleTextsOnChange(object sender, FileSystemEventArgs fileSystemEventArgs)
		{
			_sampleTextsChanged = true;
		}

		public void Dispose()
		{
			if (_disposed) return;
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (_sampleTextsWatcher != null)
				{
					_sampleTextsWatcher.EnableRaisingEvents = false;
					_sampleTextsWatcher.Dispose();
					_sampleTextsWatcher = null;
				}

				if (_tempDir != null)
				{
					_tempDir.Dispose();
					_tempDir = null;
				}
			}

			_disposed = true;
		}
	}
}
