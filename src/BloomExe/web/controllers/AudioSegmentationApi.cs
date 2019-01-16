using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Api;
using Bloom.Book;
using Newtonsoft.Json;

namespace Bloom.web.controllers
{
	// API Handler to process audio segmentation (forced alignment)
	public class AudioSegmentationApi
	{
		public const string kApiUrlPart = "audioSegmentation/";
		private const string kWorkingDirectory = "%HOMEDRIVE%\\%HOMEPATH%";	// TODO: Linux compatability
		private const string kTimingsOutputFormat = "tsv";

		BookSelection _bookSelection;
		public AudioSegmentationApi(BookSelection bookSelection)
		{
			_bookSelection = bookSelection;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "autoSegmentAudio", AutoSegment, handleOnUiThread: false, requiresSync : false);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "checkAutoSegmentDependencies", CheckAutoSegmentDependenciesMet, handleOnUiThread: false, requiresSync: false);
		}

		/// <summary>
		/// API Handler which
		/// Returns "TRUE" if success, otherwiese "FALSE" followed by an error message if not successful.
		/// </summary>
		/// <param name="request"></param>
		public void CheckAutoSegmentDependenciesMet(ApiRequest request)
		{
			// For Auto segment to work, we need at least:
			// 1) Python (to run Aeneas)
			// 2) Aeneas (to run splits)
			// 3) Any Aeneas dependencies, but hopefully the install of Aeneas took care of that
			//    3A) Espeak is one.
			//    3B) FFMMPEG is required by Aeneas, and we also use it to do splitting here as well.  (Also, Bloom uses a stripped-down version in other places)
			// 4) FFMPEG, and not necessarily a stripped-down version
			// 5) Any FFMPEG dependencies?
			string message;
			if (!AreAutoSegmentDependenciesMet(out message))
			{
				request.ReplyWithText($"FALSE {message}");
				return;
			}
			else
			{
				// Note: We could also check if an audio file exists. But I think it's best to delay that check until absolutely needed.
				// It makes the state updates when the user records or deletes audio more complicated for little gain I think.
				request.ReplyWithText("TRUE");
				return;
			}
		}

		public bool AreAutoSegmentDependenciesMet(out string message)
		{
			string formatStringDependencyMissing = L10NSharp.LocalizationManager.GetString("Common.ItemNotFound", "{0} not found.");

			if (DoesCommandCauseError("WHERE python", kWorkingDirectory))   // TODO: Linux compatability. Also more below.   Maybe use "locate" command on Linux?
			{
				message = String.Format(formatStringDependencyMissing, "Python");
				return false;
			}
			else if (DoesCommandCauseError("WHERE espeak", kWorkingDirectory))
			{
				message = String.Format(formatStringDependencyMissing, "espeak");
				return false;
			}
			else if (DoesCommandCauseError("WHERE ffmpeg", kWorkingDirectory))
			{
				message = String.Format(formatStringDependencyMissing, "FFMPEG");
				return false;
			}
			else if (DoesCommandCauseError("python -m aeneas.tools.execute_task", kWorkingDirectory, 2))    // Expected to list usage. Error Code 0 = Success, 1 = Error, 2 = Help shown.
			{
				message = String.Format(formatStringDependencyMissing, "Aeneas for Python"); 
				return false;
			}

			message = "";
			return true;
		}

		protected bool DoesCommandCauseError(string commandString, string workingDirectory = "", params int[] errorCodesToIgnore)
		{
			string stdOut;
			string stdErr;
			return DoesCommandCauseError(commandString, workingDirectory, out stdOut, out stdErr, errorCodesToIgnore);
		}

		// Returns true if the command returned with an error
		protected bool DoesCommandCauseError(string commandString, string workingDirectory, out string standardOutput, out string standardError, params int[] errorCodesToIgnore)
		{
			if (!String.IsNullOrEmpty(workingDirectory))
			{
				commandString = $"cd \"{workingDirectory}\" && {commandString}";
			}

			string arguments = $"/C {commandString} && exit %ERRORLEVEL%";
			var process = new Process()
			{
				StartInfo = new ProcessStartInfo()
				{
					FileName = "CMD",	// TODO: Linux compatability
					Arguments = arguments,
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
				},
			};

			process.Start();
			process.WaitForExit();

			standardOutput = process.StandardOutput.ReadToEnd();
			standardError = process.StandardError.ReadToEnd();

			Debug.Assert(process.ExitCode != -1073741510); // aka 0xc000013a which means that the command prompt exited, and we can't determine what the exit code of the last command was :(

			if (process.ExitCode == 0)
			{
				return false;	// No error
			}
			else if (errorCodesToIgnore != null && errorCodesToIgnore.Contains(process.ExitCode))
			{
				// It seemed to return an error, but the caller has actually specified that this error is nothing to worry about, so return no error
				return false;
			}
			else
			{
				// Error
				return true;
			}
		}

		public void AutoSegment(ApiRequest request)
		{
			// Parse the JSON containing the text segmentation data.
			var dynamicParsedObj = DynamicJson.Parse(request.RequiredPostJson());
			string filenameBase = dynamicParsedObj.audioFilenameBase;
			string directoryName = _bookSelection.CurrentSelection.FolderPath + "\\audio";
			 
			string inputAudioFilename = GetFileNameToSegment(directoryName, filenameBase);
			if (String.IsNullOrEmpty(inputAudioFilename))
			{
				request.ReplyWithText("No audio file found. Please record audio first.");
				return;
			}

			IEnumerable<IList<string>> fragmentIdTuples = (string[][])(dynamicParsedObj.fragmentIdTuples);
			string requestedLangCode = dynamicParsedObj.lang;

			string message;
			if (!AreAutoSegmentDependenciesMet(out message))
			{
				request.ReplyWithText($"Missing dependency: {message}");
				return;
			}

			// When using TTS overrides, there's no Aeneas error message that tells us if the language is unsupported.
			// Therefore, we explicitly test if the language is supported by the dependency (eSpeak) before getting started.
			string langCode = null;
			var langCodesToTry = new string[] { requestedLangCode, "eo", "en" }; // "eo" is Esperanto
			string stdOut = "";
			string stdErr = "";
			foreach (var langCodeToTry in langCodesToTry)
			{
				if (!DoesCommandCauseError($"espeak -v {langCodeToTry} -q \"hello world\"", kWorkingDirectory, out stdOut, out stdErr))
				{
					langCode = langCodeToTry;
					break;
				}
			}
			if (String.IsNullOrEmpty(langCode))
			{
				// FYI: The error message is expected to be in stdError with an empty stdOut, but I included both just in case.
				request.ReplyWithText($"eSpeak error: {stdOut}\n{stdErr}");
				return;
			}

			string textFragmentsFilename =  $"{directoryName}/{filenameBase}_fragments.txt";
			string audioTimingsFilename = $"{directoryName}/{filenameBase}_timings.{kTimingsOutputFormat}";

			fragmentIdTuples = fragmentIdTuples.Where(subarray => !String.IsNullOrWhiteSpace(subarray[0]));	// Remove entries containing only whitespace
			var fragmentList = fragmentIdTuples.Select(subarray => subarray[0]);
			var idList = fragmentIdTuples.Select(subarray => subarray[1]).ToList();

			try
			{
				File.WriteAllLines(textFragmentsFilename, fragmentList);

				var timingStartEndRangeList = GetSplitStartEndTimings(inputAudioFilename, textFragmentsFilename, audioTimingsFilename, langCode);

				ExtractAudioSegments(idList, timingStartEndRangeList, directoryName, inputAudioFilename);
			}
			catch (Exception e)
			{
				request.ReplyWithText("AutoSegment failed: " + e.Message + "\n" + e.StackTrace);
			}

			request.ReplyWithText("TRUE"); // Success
		}

		/// <summary>
		/// Given a filename base, finds the appropriate extension (if it exists) of a segmentable file.
		/// </summary>
		/// <param name="directoryName"></param>
		/// <param name="fileNameBase"></param>
		/// <returns>The file path (including directory) of a valid file if it exists, or null otherwise</returns>
		private string GetFileNameToSegment(string directoryName, string fileNameBase)
		{
			var extensions = new string[] { "mp3", "wav" };

			foreach (var extension in extensions)
			{
				string filePath = $"{directoryName}\\{fileNameBase}.{extension}";

				if (File.Exists(filePath))
				{
					return filePath;
				}
			}

			return null;
		}

		public List<Tuple<string, string>> GetSplitStartEndTimings(string inputAudioFilename, string inputTextFragmentsFilename, string outputTimingsFilename, string ttsEngineLang = "en")
		{
			// Just setting some default value here (Esperanto - which is more phonetic so we think it works well for a large variety),
			// but really rely-ing on the TTS override to specify the real lang, so this value doesn't really matter.
			string aeneasLang = "eo";

			// Note: The version of FFMPEG in output/Debug or output/Release is probably not compatible with the version required by Aeneas.
			// Thus, change the working path to something that hopefully doesn't contain our FFMPEG version.
			string changeDirectoryCommand = $"cd {kWorkingDirectory} && ";

			// I think this sets the boundary to the midpoint between the end of the previous sentence and the start of the next one.
			// This is good because by default, it would align it such that the subsequent audio started as close as possible to the beginning of it. Since there is a subtle pause when switching between two audio files, this left very little margin for error.
			string boundaryAdjustmentParams = "|task_adjust_boundary_algorithm=percent|task_adjust_boundary_percent_value=50";

			// This identifies a "head" region of 0-12 seconds of silence/non-intelligible, which will prevent it from being included in the first sentence's audio. (FYI, the hidden format will suppress it from the output timings file).
			string audioHeadParams = "|os_task_file_head_tail_format=hidden|is_audio_file_detect_head_min=0.00|is_audio_file_detect_head_max=12.00";
			string commandString = $"{changeDirectoryCommand} python -m aeneas.tools.execute_task \"{inputAudioFilename}\" \"{inputTextFragmentsFilename}\" \"task_language={aeneasLang}|is_text_type=plain|os_task_file_format={kTimingsOutputFormat}{audioHeadParams}{boundaryAdjustmentParams}\" \"{outputTimingsFilename}\" --runtime-configuration=\"tts_voice_code={ttsEngineLang}\"";

			var processStartInfo = new ProcessStartInfo()
			{
				FileName = "CMD.EXE",	// TODO: Linux compatability

				// DEBUG NOTE: you can use "/K" instead of "/C" to keep the window open (if needed for debugging)
				Arguments = $"/C {commandString}"
			};

			var process = Process.Start(processStartInfo);

			// TODO: Should I set a timeout?  In general Aeneas is reasonably fast but it doesn't really seem like I could guarantee that it would return within a certain time..
			// Block the current thread of execution until aeneas has completed, so that we can read the correct results from the output file.
			process.WaitForExit();

			// Note: we could also request Aeneas write the standard output/error, or a log (or verbose log... or very verbose log) if desired


			// This might throw exceptions, but IMO best to let the error handler pass it, and have the Javascript code be as robust as possible, instead of passing on error messages to user
			var segmentationResults = File.ReadAllLines(outputTimingsFilename);

			List<Tuple<string, string>> timingStartEndRangeList;
			if (kTimingsOutputFormat.Equals("srt", StringComparison.OrdinalIgnoreCase))
			{
				timingStartEndRangeList = ParseTimingFileSRT(segmentationResults);
			}
			else
			{
				timingStartEndRangeList = ParseTimingFileTSV(segmentationResults);
			}
			
			return timingStartEndRangeList;
		}


		/// <summary>
		/// Parses the contents of a timing file and returns the start and end timing fields as a list of tuples.
		/// </summary>
		/// <param name="segmentationResults">The contents (line-by-line) of a .tsv timing file. Example: "1.000\t4.980\tf000001"</param>
		private List<Tuple<string, string>> ParseTimingFileTSV(IEnumerable<string> segmentationResults)
		{
			var timings = new List<Tuple<string, string>>();

			foreach (string line in segmentationResults)
			{
				var fields = line.Split('\t');
				string timingStart = (fields.Length > 0 ? fields[0].Trim() : null);
				string timingEnd = (fields.Length > 1 ? fields[1].Trim() : null);

				if (String.IsNullOrEmpty(timingStart))
				{
					if (!timings.Any())
					{
						timingStart = "0.000";
					}
					else
					{
						timingStart = timings.Last().Item2;
					}
				}

				// If timingEnd is messed up, we'll continue to pass the record. In theory, it is valid for the timings to be defined solely by timingStart without specifying an explicit timingEnd (as long as you don't need the highlight to disappear for a time)
				// timingEnd is easily inferred as the next timingStart
				// so don't remove records if timingEnd is missing
				timings.Add(Tuple.Create(timingStart, timingEnd));
			}

			return timings;
		}

		/// <summary>
		/// Parses the contents of a timing file and returns the start and end timing fields as a list of tuples.
		/// </summary>
		/// <param name="segmentationResults">The contents (line-by-line) of a .srt timing file</param>
		private List<Tuple<string, string>> ParseTimingFileSRT(IList<string> segmentationResults)
		{
			var timings = new List<Tuple<string, string>>();

			// For now, just a simple parser that assumes the input is very well-formed, no attempt to re-align the states or anything
			// Each record comes in series of 4 lines. The first line has the fragment index (1-based), then the timing range, then the text, then a newline
			// We really only need the timing range for now so we just go straight to it and skip over everything else
			for (int lineNumber = 1; lineNumber <= segmentationResults.Count; lineNumber += 4)
			{
				string line = segmentationResults[lineNumber];
				string timingRange = line.Replace(',', '.');    // Convert from SRT's/Aeneas's HH:MM::SS,mmm format to FFMPEG's "HH:MM:SS.mmm" format. (aka European decimal points to American decimal points). This SRT format seems independent of system locale.
				var fields = timingRange.Split(new string[] { "-->" }, StringSplitOptions.None);
				string timingStart = fields[0].Trim();
				string timingEnd = fields[1].Trim();

				if (String.IsNullOrEmpty(timingStart))
				{
					if (!timings.Any())
					{
						timingStart = "00:00:00.000";
					}
					else
					{
						timingStart = timings.Last().Item2;
					}
				}

				// If timingEnd is messed up, we'll continue to pass the record. In theory, it is valid for the timings to be defined solely by timingStart without specifying an explicit timingEnd (as long as you don't need the highlight to disappear for a time)
				// timingEnd is easily inferred as the next timingStart
				// so don't remove records if timingEnd is missing
				timings.Add(Tuple.Create(timingStart, timingEnd));
			}

			return timings;
		}

		/// <summary>
		/// Given a list of timings, segments a whole audio file into the individual pieces specified by the timing list
		/// </summary>
		/// <param name="idList"></param>
		/// <param name="timingStartEndRangeList"></param>
		/// <param name="directoryName"></param>
		/// <param name="inputAudioFilename"></param>
		private void ExtractAudioSegments(IList<string> idList, IList<Tuple<string, string>> timingStartEndRangeList, string directoryName, string inputAudioFilename)
		{
			Debug.Assert(idList.Count == timingStartEndRangeList.Count, $"Number of text fragments ({idList.Count}) does not match number of extracted timings ({timingStartEndRangeList.Count}). The parsed timing ranges might be completely incorrect. The last parsed timing is: ({timingStartEndRangeList.Last()?.Item1 ?? "null"}, {timingStartEndRangeList.Last()?.Item2 ?? "null"}).");

			string extension = Path.GetExtension(inputAudioFilename);	// Will include the "." e.g. ".mp3"
			if (string.IsNullOrWhiteSpace(extension))
			{
				extension = ".mp3";
			}
			// Allow each ffmpeg to run in parallel
			var tasksToWait = new Task[timingStartEndRangeList.Count];
			for (int i = 0; i < timingStartEndRangeList.Count; ++i)
			{
				var timingRange = timingStartEndRangeList[i];
				var timingStartString = timingRange.Item1;
				var timingEndString = timingRange.Item2;

				string splitFilename = $"{directoryName}/{idList[i]}{extension}";

				tasksToWait[i] = ExtractAudioSegmentAsync(inputAudioFilename, timingStartString, timingEndString, splitFilename);
			}

			// TODO: Need to report an error or exception or do a retry if a task fails.

			// Wait for them all so that the UI knows all the files are there before it starts mucking with the HTML structure.
			Task.WaitAll(tasksToWait.ToArray());
		}

		/// <summary>
		/// Given a single timing, extract the specified segment of audio
		/// </summary>
		/// <param name="inputAudioFilename"></param>
		/// <param name="timingStartString"></param>
		/// <param name="timingEndString"></param>
		/// <param name="outputSplitFilename"></param>
		/// <returns></returns>
		public Task<int> ExtractAudioSegmentAsync(string inputAudioFilename, string timingStartString, string timingEndString, string outputSplitFilename)
		{
			string commandString = $"cd {kWorkingDirectory} && ffmpeg -i \"{inputAudioFilename}\" -acodec copy -ss {timingStartString} -to {timingEndString} \"{outputSplitFilename}\"";
			var startInfo = new ProcessStartInfo(fileName: "CMD", arguments: $"/C {commandString}");	// TODO: Linux compatability

			return RunProcessAsync(startInfo);
		}

		// Starts a process and returns a task (that you can use to wait/await for the completion of the process0
		public static Task<int> RunProcessAsync(ProcessStartInfo startInfo)
		{
			var tcs = new TaskCompletionSource<int>();

			var process = new Process
			{
				StartInfo = startInfo,
				EnableRaisingEvents = true
			};

			process.Exited += (sender, args) =>
			{
				tcs.SetResult(process.ExitCode);
				process.Dispose();
			};

			process.Start();

			return tcs.Task;
		}
	}
}
