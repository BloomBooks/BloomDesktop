using System;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom.Edit;
using SIL.IO;
using SIL.Progress;
using SIL.Xml;

namespace Bloom.Publish
{
	public class AudioProcessor
	{

		private static LameEncoder _mp3Encoder;

		//extracted so unit test can override
		public static Func<string, string> _compressorMethod = MakeCompressedAudio;

		public static bool IsAnyCompressedAudioMissing(string bookFolderPath, XmlDocument dom)
		{
			return !GetTrueForAllAudioSpans(bookFolderPath, dom,
				(wavpath, mp3path) => !RobustFile.Exists(wavpath) || RobustFile.Exists(mp3path));
		}



		/// <summary>
		/// Compress all the existing wav files into mp3s, it they aren't already compressed
		/// </summary>
		/// <returns>true if everything is compressed</returns>
		public static bool TryCompressingAudioAsNeeded(string bookFolderPath, XmlDocument dom)
		{
			return GetTrueForAllAudioSpans(bookFolderPath, dom,
				(wavpath, mp3path) =>
				{
					if (RobustFile.Exists(wavpath) && !RobustFile.Exists(mp3path))
					{
						return MakeCompressedAudio(wavpath) != null;
					}
					return true; // already have the mp3
				});
		}

		private static bool GetTrueForAllAudioSpans(string bookFolderPath, XmlDocument dom, Func<string, string, bool> predicate)
		{
			var audioFolderPath = GetAudioFolderPath(bookFolderPath);
			return dom.SafeSelectNodes("//span[@id]")
				.Cast<XmlElement>()
				.All(span =>
				{
					var wavpath = Path.Combine(audioFolderPath, Path.ChangeExtension(span.Attributes["id"].Value, "wav"));
					var mp3path = Path.ChangeExtension(wavpath, "mp3");
					return predicate(wavpath, mp3path);
				});
		}

		private static string GetAudioFolderPath(string bookFolderPath)
		{
			return Path.Combine(bookFolderPath, "audio");
		}

		/// <summary>
		/// Make a compressed audio file for the specified .wav file.
		/// (Or return null if it can't be done because we don't have a LAME package installed.)
		/// </summary>
		/// <param name="id"></param>
		/// <returns></returns>
		// internal and virtual for testing.
		private static string MakeCompressedAudio(string wavPath)
		{
			// We have a recording, but not compressed. Possibly the LAME package was installed after
			// the recordings were made. Compress it now.
			if(_mp3Encoder == null)
			{
				if(!LameEncoder.IsAvailable())
				{
					return null;
				}
				_mp3Encoder = new LameEncoder();
			}
			_mp3Encoder.Encode(wavPath, wavPath.Substring(0, wavPath.Length - 4), new NullProgress());
			return Path.ChangeExtension(wavPath, "mp3");
		}

		public static string GetOrCreateCompressedAudioIfWavExists(string bookFolderPath, string recordingSegmentId)
		{
			var root = GetAudioFolderPath(bookFolderPath);
			var extensions = new[] {"mp3", "mp4"}; // .ogg,, .wav, ...?

			foreach(var ext in extensions)
			{
				var path = Path.Combine(root, Path.ChangeExtension(recordingSegmentId, ext));
				if(RobustFile.Exists(path))
					return path;
			}
			var wavPath = Path.Combine(root, Path.ChangeExtension(recordingSegmentId, "wav"));
			if(!RobustFile.Exists(wavPath))
				return null;
			return _compressorMethod(wavPath);
		}

		public static bool GetWavOrMp3Exists(string bookFolderPath, string recordingSegmentId)
		{
			var root = GetAudioFolderPath(bookFolderPath);
			var extensions = new[] {"wav", "mp3"}; // .ogg,, .wav, ...?

			foreach(var ext in extensions)
			{
				var path = Path.Combine(root, Path.ChangeExtension(recordingSegmentId, ext));
				if(RobustFile.Exists(path))
					return true;
			}
			return false;
		}
	}
}
