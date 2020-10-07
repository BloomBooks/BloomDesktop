using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Bloom.Edit;
using SIL.CommandLineProcessing;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;

namespace Bloom.Publish
{
	public class AudioProcessor
	{
		// We record as .wav but convert to .mp3 for publication
		public static readonly string[] NarrationAudioExtensions = {".wav", ".mp3"};

		public static readonly string[] MusicFileExtensions = {".mp3", ".ogg", ".wav"};

		public static readonly string[] AudioFileExtensions = NarrationAudioExtensions.Union(MusicFileExtensions).ToArray();

		private static LameEncoder s_mp3Encoder;

		/// <summary>
		/// Compares timestamps on .wav files and .mp3 files to see if we need to update any .mp3 files.
		/// Be aware that if you are staging audio files in preparation for publishing, the bookFolderPath
		/// should be the original book folder files, not the staged files. Otherwise you may get unexpected
		/// results. (See BL-5437)
		/// </summary>
		public static bool IsAnyCompressedAudioMissing(string bookFolderPath, XmlDocument dom)
		{
			return !IsTrueForAllAudioSentenceElements(bookFolderPath, dom,
				(recordablePath, publishablePath) => !IsPublishableAudioNeeded(recordablePath, publishablePath));
		}

		/// <summary>
		/// Compress all the existing wav files into mp3s, if they aren't already compressed
		/// </summary>
		/// <returns>true if everything is compressed</returns>
		public static bool TryCompressingAudioAsNeeded(string bookFolderPath, XmlDocument dom)
		{
			var watch = Stopwatch.StartNew();
			bool result = IsTrueForAllAudioSentenceElements(bookFolderPath, dom,
				(recordablePath, publishablePath) =>
				{
					if (IsPublishableAudioNeeded(recordablePath, publishablePath))
					{
						return MakeCompressedAudio(recordablePath) != null;
					}
					return true; // already have an up-to-date mp3 (or can't make one because there's no wav)
				});
			watch.Stop();
			Debug.WriteLine("compressing audio took " + watch.ElapsedMilliseconds);
			return result;
		}

		// We only need to make an MP3 if we actually have a corresponding wav file.
		// Assuming we have a wav file and thus want a corresponding mp3, we need to make it if either it does not exist
		// or it is out of date (older than the wav file).
		// It's of course possible that although it is newer the two don't correspond. I don't know any way to reliably prevent that
		// except to regenerate them all on every publish event, but that is quite time-consuming.
		private static bool IsPublishableAudioNeeded(string recordablePath, string publishablePath)
		{
			return RobustFile.Exists(recordablePath) &&
			       (!RobustFile.Exists(publishablePath) || (new FileInfo(recordablePath).LastWriteTimeUtc) > new FileInfo(publishablePath).LastWriteTimeUtc);
		}

		private static bool IsTrueForAllAudioSentenceElements(string bookFolderPath, XmlDocument dom, Func<string, string, bool> predicate)
		{
			var audioFolderPath = GetAudioFolderPath(bookFolderPath);
			return Book.HtmlDom.SelectAudioSentenceElements(dom.DocumentElement)
				.Cast<XmlElement>()
				.All(span =>
				{
					var recordablePath = Path.Combine(audioFolderPath, Path.ChangeExtension(span.Attributes["id"]?.Value, AudioRecording.kRecordableExtension));
					var publishablePath = Path.ChangeExtension(recordablePath, AudioRecording.kPublishableExtension);
					return predicate(recordablePath, publishablePath);
				});
		}

		internal static string GetAudioFolderPath(string bookFolderPath)
		{
			return Path.Combine(bookFolderPath, "audio");
		}

		/// <summary>
		/// Make a compressed audio file for the specified .wav file.
		/// </summary>
		public static string MakeCompressedAudio(string wavPath)
		{
			// We have a recording, but not compressed. Possibly the LAME package was installed after
			// the recordings were made. Compress it now.
			if(s_mp3Encoder == null)
				s_mp3Encoder = new LameEncoder();
			return s_mp3Encoder.Encode(wavPath);
		}

		public static string GetOrCreateCompressedAudio(string bookFolderPath, string recordingSegmentId)
		{
			if (string.IsNullOrEmpty(recordingSegmentId))
				return null;

			var root = GetAudioFolderPath(bookFolderPath);

			var publishablePath = Path.Combine(root, Path.ChangeExtension(recordingSegmentId, AudioRecording.kPublishableExtension));
			if (RobustFile.Exists(publishablePath))
				return publishablePath;

			var recordablePath = Path.ChangeExtension(publishablePath, AudioRecording.kRecordableExtension);
			if (!RobustFile.Exists(recordablePath))
				return null;
			return MakeCompressedAudio(recordablePath);
		}

		public static bool DoesAudioExistForSegment(string bookFolderPath, string recordingSegmentId)
		{
			if (recordingSegmentId == null)
			{
				return false;
			}

			var root = GetAudioFolderPath(bookFolderPath);

			foreach(var ext in NarrationAudioExtensions)
			{
				var path = Path.Combine(root, Path.ChangeExtension(recordingSegmentId, ext));
				if(RobustFile.Exists(path))
					return true;
			}
			return false;
		}

		/// <summary>
		/// Merge the specified input files into the specified output file. Returns null if successful,
		/// a string that may be useful in debugging if not.
		/// </summary>
		public static string MergeAudioFiles(IEnumerable<string> mergeFiles, string combinedAudioPath)
		{
			var ffmpeg = "/usr/bin/ffmpeg"; // standard Linux location
			if (SIL.PlatformUtilities.Platform.IsWindows)
				ffmpeg = Path.Combine(BloomFileLocator.GetCodeBaseFolder(), "ffmpeg.exe");
			if (RobustFile.Exists(ffmpeg))
			{
				// A command something like
				//   ffmpeg -i "concat:stereo1.mp3|monaural2.mp3|stereo3.mp3" -c:a libmp3lame output.mp3
				// might work okay except that the output file characteristics appear to depend on the
				// first file in the list, and stereo recorded on only one side does come out only on
				// that one side if the file ends up being stereo, but not if the file ends up monaural.
				// This may or may not be preferable to flattening all of the files to monaural at the
				// standard recording rate before concatenating them.  The current approach at least
				// has deterministic output for all files.
				var monoFiles = TryEnsureConsistentInputFiles(ffmpeg, mergeFiles);
				try
				{
					var argsBuilder = new StringBuilder("-i \"concat:");
					argsBuilder.Append(string.Join("|", monoFiles));
					argsBuilder.Append($"\" -c copy \"{combinedAudioPath}\"");
					var result = CommandLineRunner.Run(ffmpeg, argsBuilder.ToString(), "", 60 * 10, new NullProgress());
					var output = result.ExitCode != 0 ? result.StandardError : null;
					if (output != null)
					{
						RobustFile.Delete(combinedAudioPath);
					}
					return output;
				}
				finally
				{
					foreach (var file in monoFiles)
					{
						if (!mergeFiles.Contains(file))
							RobustFile.Delete(file);
					}
				}
			}

			return "Could not find ffmpeg";
		}

		/// <summary>
		/// Epub preview cannot play an audio file that contains a mixture of stereo and monaural
		/// sections.  It also cannot play an audio file that contains segments recorded at
		/// different rates.  We concatenate all narration files used on a page together for epubs
		/// to use, so we need to ensure that resulting audio file is all of one type at one rate.
		/// (which we default to monaural at 44100 Hz, which seems to be a standard default)
		/// Scan through the input file list, checking whether each file has been recorded in stereo
		/// or mono.  If it was recorded in stereo, create a monaural version of the file for
		/// concatenating with the other files in the list.  If the file was recorded at a rate
		/// other than 44100 Hz, create a version with that recording rate.
		/// If there's only one file in the list, just return the input list.
		/// If any file fails to convert for any reason, it is returned in the output list.  So the
		/// files in the output list may not really be consistent in reality...
		/// </summary>
		/// <remarks>
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-9051
		/// and https://issues.bloomlibrary.org/youtrack/issue/BL-9100.
		/// </remarks>
		private static IEnumerable<string> TryEnsureConsistentInputFiles(string ffmpeg, IEnumerable<string> mergeFiles)
		{
			if (mergeFiles.Count() < 2)
				return mergeFiles;
			var monoFiles = new List<string>();
			var argsBuilder = new StringBuilder();
			foreach (var file in mergeFiles)
			{
				var args = $"-hide_banner -i \"{file}\" -f null -";
				var result = CommandLineRunner.Run(ffmpeg, args, "", 60, new NullProgress());
				var output = result.StandardError;
				var match = Regex.Match(output, "Audio: [^,]*, ([0-9]+) Hz, ([a-z]+), [^,]*, ([0-9]+) kb/s");
				if (match.Success)
				{
					// Get a mono version at 44100 Hz of the sound file, trying to preserve its quality.
					var recordRate = match.Groups[1].ToString();
					var recordType = match.Groups[2].ToString();
					var bitRate = match.Groups[3].ToString();
					if (recordType == "stereo" || recordRate != "44100")
					{
						var tempFile = TempFile.CreateAndGetPathButDontMakeTheFile();
						tempFile.Detach();
						args = $"-i \"{file}\" -ac 1 -ar 44100 -b:a {bitRate}k \"{tempFile.Path}.mp3\"";
						Debug.WriteLine($"DEBUG: ffmpeg {args}");
						result = CommandLineRunner.Run(ffmpeg, args, "", 120, new NullProgress());
						if (result.ExitCode == 0)
						{
							monoFiles.Add(tempFile.Path + ".mp3");
							continue;
						}
						else
						{
							Logger.WriteEvent($"Error converting {file} to monaural at 44100 Hz" + Environment.NewLine + result.StandardError);
							Debug.WriteLine($"Error converting {file} to monaural at 44100 Hz");
							Debug.WriteLine(result.StandardError);
						}
					}
				}
				monoFiles.Add(file);
			}
			return monoFiles;
		}

		public static bool HasAudioFileExtension(string fileName)
		{
			var extension = Path.GetExtension(fileName);
			return !String.IsNullOrEmpty(extension) && AudioFileExtensions.Contains(extension.ToLowerInvariant());
		}

		public static bool HasBackgroundMusicFileExtension(string fileName)
		{
			var extension = Path.GetExtension(fileName);
			return !String.IsNullOrEmpty(extension) && MusicFileExtensions.Contains(extension.ToLowerInvariant());
		}
	}
}
