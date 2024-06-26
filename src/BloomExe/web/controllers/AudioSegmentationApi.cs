using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;
using Bloom.ToPalaso;
using Bloom.Utils;
using BloomTemp;
using Newtonsoft.Json;
using SIL.Code;
using SIL.IO;
using SIL.PlatformUtilities;
using SIL.Progress;
using SIL.Reporting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.web.controllers
{
    internal class AutoSegmentRequest
    {
        public string audioFilenameBase;
        public AudioTextFragment[] audioTextFragments;
        public string lang;
        public string manualTimingsPath;
    }

    internal class AutoSegmentResponse
    {
        public string allEndTimesString;
        public string timingsFilePath;
        public string warningMessage;
        public string successMessage;
    }

    internal class AudioTextFragment
    {
        public string fragmentText;
        public string id;
    }

    internal class ESpeakPreviewRequest
    {
        public string text;
        public string lang;
    }

    // API Handler to process audio segmentation (forced alignment)
    public class AudioSegmentationApi
    {
        public const string kApiUrlPart = "audioSegmentation/";
        private const string kWorkingDirectory = "%HOMEDRIVE%\\%HOMEPATH%"; // Linux will use "/tmp" when the working directory doesn't matter

        // 0 could be a useful value for this if you hard-split. (on the rationale that you won't accidentally cut off anything useful).
        // For soft splits, you might as well set this to something useful.
        // Allowing a head period improves identification of when the 1st fragment's audio begins,
        // which seems to improve identification of when the that fragment ENDS.
        // For soft splits, there's not really a huge cost to if the head period is a little too long
        // because the 1st fragment is highlighted even during the head period
        private const float maxAudioHeadDurationSec = 5; // maximum potentially allowable length in seconds of the non-useful "head" part of the audio which Aeneas will attempt to identify (if it exists) and then exclude from the timings

        BookSelection _bookSelection;

        public AudioSegmentationApi(BookSelection bookSelection)
        {
            _bookSelection = bookSelection;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "checkAutoSegmentDependencies",
                CheckAutoSegmentDependenciesMet,
                handleOnUiThread: false,
                requiresSync: false
            );
            // JH: this is unused: apiHandler.RegisterEndpointLegacy(kApiUrlPart + "autoSegmentAudio", AutoSegment, handleOnUiThread: true/* may ask for a file*/, requiresSync: false);
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "getForcedAlignmentTimings",
                GetForcedAlignmentTimings,
                handleOnUiThread: false,
                requiresSync: false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "eSpeakPreview",
                ESpeakPreview,
                handleOnUiThread: false,
                requiresSync: false
            );
        }

        #region CheckAutoSegmentDependenciesMet
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
                request.ReplyWithText("FALSE");
            }
            else
            {
                // Note: We could also check if an audio file exists. But I think it's best to delay that check until absolutely needed.
                // It makes the state updates when the user records or deletes audio more complicated for little gain I think.
                Logger.WriteMinorEvent(
                    "AudioSegmentationApi.CheckAutoSegmentDependenciesMet: All dependencies met."
                );
                request.ReplyWithText("TRUE");
            }
        }

        // Save the values written to stdout which checking for dependencies.
        // These may be used later in an error log message to help diagnose what went wrong when running aeneas.
        private string _pythonFound;
        private string _espeakFound;
        private string _ffmpegFound;
        private string _aeneasInfo;

        /// <summary>
        /// Checks if all dependencies necessary to run Split feature are present.
        /// </summary>
        /// <param name="message">output - Will return which dependency is missing</param>
        /// <returns>True if all dependencies are met, meaning Split feature should be able to run successfully. False if Split feature is missing a dependency</returns>
        public bool AreAutoSegmentDependenciesMet(out string message)
        {
            string locateCmd = Platform.IsLinux ? "/usr/bin/which" : "WHERE";
            string workingDir = Platform.IsLinux ? "/tmp" : kWorkingDirectory;
            string stdout;
            var findPythonCommand = $"{locateCmd} python";
            if (
                Platform.IsLinux
                && !DoesCommandCauseError("/usr/bin/lsb_release -r", out stdout, workingDir)
            )
            {
                // Ubuntu 20.04 installs python3 by default without any /usr/bin/python, and python3-aeneas is
                // installed instead of python-aeneas (which depends on python2.7).
                // Earlier versions of Ubuntu installed /usr/bin/python as a symbolic link to /usr/bin/python2.7
                // (the regex pattern matches 20.04 through 98.04, which is the current pattern of LTS releases)
                if (
                    System.Text.RegularExpressions.Regex.IsMatch(
                        stdout.Trim(),
                        "[2-9][02468]\\.04$"
                    )
                )
                    findPythonCommand = $"{locateCmd} python3";
            }
            if (DoesCommandCauseError(findPythonCommand, out stdout, workingDir))
            {
                message = "Python";
                Logger.WriteEvent(
                    "Discovered a missing dependency for AutoSegment function: " + message
                );
                return false;
            }
            _pythonFound = stdout.Trim();

            if (DoesCommandCauseError($"{locateCmd} espeak", out stdout, workingDir))
            {
                message = "espeak";
                Logger.WriteEvent(
                    "Discovered a missing dependency for AutoSegment function: " + message
                );
                return false;
            }
            _espeakFound = stdout.Trim();

            if (DoesCommandCauseError($"{locateCmd} ffmpeg", out stdout, workingDir))
            {
                message = "FFMPEG";
                Logger.WriteEvent(
                    "Discovered a missing dependency for AutoSegment function: " + message
                );
                return false;
            }
            _ffmpegFound = stdout.Trim();

            string pythonCmd = Platform.IsLinux ? _pythonFound : "python";
            if (
                DoesCommandCauseError(
                    $"{pythonCmd} -m aeneas.tools.execute_task",
                    out stdout,
                    workingDir,
                    2
                )
            ) // Expected to list usage. Error Code 0 = Success, 1 = Error, 2 = Help shown.
            {
                message = "Aeneas for Python";
                Logger.WriteEvent(
                    "Discovered a missing dependency for AutoSegment function: " + message
                );
                return false;
            }
            _aeneasInfo = stdout.Trim();

            message = "";
            return true;
        }

        protected bool DoesCommandCauseError(
            string commandString,
            out string stdOut,
            string workingDirectory = "",
            params int[] errorCodesToIgnore
        )
        {
            string stdErr;
            return DoesCommandCauseError(
                commandString,
                workingDirectory,
                out stdOut,
                out stdErr,
                errorCodesToIgnore
            );
        }

        // Returns true if the command returned with an error
        protected bool DoesCommandCauseError(
            string commandString,
            string workingDirectory,
            out string standardOutput,
            out string standardError,
            params int[] errorCodesToIgnore
        )
        {
            string command;
            string arguments;
            if (Platform.IsLinux)
            {
                var idx = commandString.IndexOf(' ');
                if (idx < 0)
                {
                    command = commandString;
                    arguments = "";
                }
                else
                {
                    command = commandString.Substring(0, idx);
                    arguments = commandString.Substring(idx + 1);
                }
            }
            else
            {
                // REVIEW: Why run CMD.EXE instead of running the command directly?  is it needed for PATH search?
                command = "CMD.EXE";
                arguments = $"/C {commandString} ; exit %ERRORLEVEL%";
                if (!string.IsNullOrEmpty(workingDirectory) && workingDirectory.Contains("%"))
                    workingDirectory = Environment.ExpandEnvironmentVariables(workingDirectory);
            }

            if (workingDirectory == null || !Directory.Exists(workingDirectory))
                workingDirectory = "";
            var setPythonEncoding = SetPythonEncodingIfNeeded();
            var result = CommandLineRunnerExtra.RunWithInvariantCulture(
                command,
                arguments,
                workingDirectory,
                600,
                new SIL.Progress.NullProgress()
            );
            if (setPythonEncoding)
                Environment.SetEnvironmentVariable(PythonIoEncodingKey, null);

            standardOutput = result.StandardOutput;
            standardError = result.StandardError;

            Debug.Assert(
                result.ExitCode != -1073741510,
                "Process Exit Code was 0xc000013a, indicating that the command prompt exited. That means we can't read the value of the exit code of the last command of the session"
            );

            if (result.ExitCode == 0)
            {
                return false; // No error
            }
            else if (errorCodesToIgnore != null && errorCodesToIgnore.Contains(result.ExitCode))
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
        #endregion

        #region Split (AutoSegment) and supporting functionality
        // e.g. {"audioFilenameBase":"i7e1bb1ee-515e-4105-9873-9ba882b09713","audioTextFragments":[{"fragmentText":"Sentence 1.","id":"i0012e528-97d6-4d82-a862-c7c2d07c8c40"},{"fragmentText":"Sentence 2.","id":"b0bfe4a7-470c-4442-aaba-9a248e0a476d"},{"fragmentText":"Sentence 3.","id":"dfd20683-aea3-47af-8686-7714c0b354c5"}],"lang":"en"}
        internal static AutoSegmentRequest ParseJson(string json)
        {
            var request = JsonConvert.DeserializeObject<AutoSegmentRequest>(json);
            return request;
        }


#if OBSOLETEAPI
        /// API Handler when the Auto Segment button is clicked
        ///
        /// Replies with true if AutoSegment completed successfully, or false if there was an error. In addition, a NonFatal message/exception may be reported with the error
        /// </summary>
        /* (JH) I don't know why this exists, it is unused  (SMc) it supported the obsolete autoSegmentAudio API call
        public void AutoSegment(ApiRequest request)
        {
            Logger.WriteEvent("AudioSegmentationApi.AutoSegment(): AutoSegment started.");

            // Parse the JSON containing the text segmentation data.
            string json = request.RequiredPostJson();
            AutoSegmentRequest requestParameters = ParseJson(json);

            string audioFilenameToSegment;
            List<string> fragmentIds;
            var timingStartEndRangeList = GetAeneasTimings(requestParameters, out audioFilenameToSegment, out fragmentIds);

            if (timingStartEndRangeList == null)
            {
                request.ReplyWithBoolean(false);
                return;
            }

            try
            {
                ExtractAudioSegments(fragmentIds, timingStartEndRangeList, audioFilenameToSegment);
            }
            catch (Exception e)
            {
                ProblemReportApi.ShowProblemDialog(null, e,
                    "AudioSegmentationApi.AutoSegment(): Exception thrown during split/extract stage", "nonfatal");
                request.ReplyWithBoolean(false);
                return;
            }

            Logger.WriteEvent("AudioSegmentationApi.AutoSegment(): Completed successfully.");

            request.ReplyWithBoolean(true); // Success
        }
        */
#endif

        internal AutoSegmentResponse GetAeneasTimings(AutoSegmentRequest requestParameters)
        {
            List<string> fragmentIds = null;
            var response = new AutoSegmentResponse
            {
                timingsFilePath = null,
                allEndTimesString = null,
                successMessage = "",
                warningMessage = "",
            };

            // The client was supposed to validate this already, but double-check in case something strange happened.
            string directoryName = GetAudioDirectory();

            if (!Platform.IsLinux)
            {
                if (directoryName.StartsWith("\\"))
                {
                    // I'm intentionally not adding this to the l10n load, as it seems like a pretty sophisticated thing, to be running in a VM
                    // or directly off a server, and it seems a bit hard to translate.  Ref BL-9959.
                    ErrorReport.NotifyUserOfProblem(
                        "Sorry, Bloom cannot split timings if the collection's path does not start with a drive letter. Feel free to contact us for more help."
                            + "\r\n\r\n"
                            + GetAudioDirectory()
                    );
                    //audioFilenameToSegment = null;
                    return null;
                }
            }

            var audioFilenameToSegment = GetFileNameToSegment(
                directoryName,
                requestParameters.audioFilenameBase
            );
            if (string.IsNullOrEmpty(audioFilenameToSegment))
            {
                Logger.WriteEvent(
                    "AudioSegmentationApi.GetAeneasTimings(): No input audio file found."
                );
                ErrorReport.ReportNonFatalMessageWithStackTrace(
                    "No audio file found. Please record audio first."
                );
                return null;
            }

            IEnumerable<AudioTextFragment> audioTextFragments =
                requestParameters.audioTextFragments;
            string requestedLangCode = requestParameters.lang;

            // The client was supposed to validate this already, but double-check in case something strange happened.
            // Since this is basically a desperate fallback that shouldn't ever happen we won't try to make the message
            // contain a hot link here. That code is in Typescript.
            string message;
            if (!AreAutoSegmentDependenciesMet(out message))
            {
                var localizedFormatString = L10NSharp.LocalizationManager.GetString(
                    "EditTab.Toolbox.TalkingBook.MissingDependency",
                    "To split recordings into sentences, first install this {0} system.",
                    "The placeholder {0} will be replaced with the dependency that needs to be installed."
                );
                ErrorReport.ReportNonFatalMessageWithStackTrace(
                    string.Format(localizedFormatString, message)
                );
                return null;
            }

            // When using TTS overrides, there's no Aeneas error message that tells us if the language is unsupported.
            // Therefore, we explicitly test if the language is supported by the dependency (eSpeak) before getting started.
            string stdOut;
            string stdErr;
            string langCode = GetBestSupportedLanguage(requestedLangCode, out stdOut, out stdErr);

            if (string.IsNullOrEmpty(langCode))
            {
                // FYI: The error message is expected to be in stdError with an empty stdOut, but I included both just in case.
                Logger.WriteEvent("AudioSegmentationApi.GetAeneasTimings(): eSpeak error.");
                ErrorReport.ReportNonFatalMessageWithStackTrace(
                    $"eSpeak error: {stdOut}\n{stdErr}"
                );
                return null;
            }
            Logger.WriteMinorEvent(
                $"AudioSegmentationApi.GetAeneasTimings(): Attempting to segment with langCode={langCode}"
            );

            string textFragmentsFilename = Path.Combine(
                directoryName,
                $"{requestParameters.audioFilenameBase}_fragments.txt"
            );

            // Clean up the fragments
            audioTextFragments = audioTextFragments.Where(
                obj => !String.IsNullOrWhiteSpace(obj.fragmentText)
            ); // Remove entries containing only whitespace
            var fragmentList = audioTextFragments.Select(
                obj => TextUtils.TrimEndNewlines(obj.fragmentText)
            );
            if (langCode != requestedLangCode)
            {
                string collectionPath = _bookSelection
                    .CurrentSelection
                    .CollectionSettings
                    .FolderPath;
                string orthographyConversionMappingPath = Path.Combine(
                    collectionPath,
                    $"convert_{requestedLangCode}_to_{langCode}.txt"
                );
                if (RobustFile.Exists(orthographyConversionMappingPath))
                {
                    fragmentList = ApplyOrthographyConversion(
                        fragmentList,
                        orthographyConversionMappingPath
                    );
                }
            }

            // Get the GUID filenames (without extension)
            fragmentIds = audioTextFragments.Select(obj => obj.id).ToList();

            List<Tuple<string, string>> timingStartEndRangeList = null;
            try
            {
                RobustFile.WriteAllLines(textFragmentsFilename, fragmentList);

                if (!string.IsNullOrEmpty(requestParameters.manualTimingsPath))
                {
                    response.timingsFilePath = Path.Combine(
                        directoryName,
                        requestParameters.manualTimingsPath
                    );
                    Logger.WriteEvent("AudioSegmentationApi applying manual timings file.");
                    var lines = RobustFile.ReadAllLines(requestParameters.manualTimingsPath);
                    timingStartEndRangeList = ParseTimingFileTSV(lines);
                    Logger.WriteEvent($"Parsed {timingStartEndRangeList.Count()} lines.");
                    if (timingStartEndRangeList.Count() != fragmentList.Count())
                    {
                        response.warningMessage =
                            $"This box has {fragmentList.Count()} text fragments, but the timings file has {timingStartEndRangeList.Count()} lines";
                    }
                    else
                    {
                        response.successMessage =
                            $"Applied {timingStartEndRangeList.Count()} manual timings.";
                    }
                }
                else
                {
                    response.timingsFilePath = Path.Combine(
                        directoryName,
                        $"{requestParameters.audioFilenameBase}_timings.txt"
                    );
                    timingStartEndRangeList = GetSplitStartEndTimings(
                        audioFilenameToSegment,
                        textFragmentsFilename,
                        response.timingsFilePath,
                        langCode
                    );
                }
            }
            catch (Exception e)
            {
                ProblemReportApi.ShowProblemDialog(
                    null,
                    e,
                    "AudioSegmentationApi.GetAeneasTimings(): Exception thrown during split stage",
                    "nonfatal"
                );
                return null;
            }

            try
            {
                RobustFile.Delete(textFragmentsFilename);
            }
            catch (Exception e)
            {
                // These exceptions are unfortunate but not bad enough that we need to inform the user
                Debug.Assert(
                    false,
                    $"Attempted to delete {textFragmentsFilename} but it threw an exception. Message={e.Message}, Stack={e.StackTrace}"
                );
            }

            /* leave it around so that people can edit it if they want
                try
                {
                    RobustFile.Delete(response.timingsFilePath);
                }
                catch (Exception e)
                {
                    // These exceptions are unfortunate but not bad enough that we need to inform the user
                    Debug.Assert(false, $"Attempted to delete {response.timingsFilePath} but it threw an exception. Message={e.Message}, Stack={e.StackTrace}");
                }
            */
            response.allEndTimesString = String.Join(
                " ",
                timingStartEndRangeList.Select(tuple => tuple.Item2)
            );
            return response;
        }

        public string GetAudioDirectory()
        {
            return Path.Combine(_bookSelection.CurrentSelection.FolderPath, "audio");
        }

        private string GetBestSupportedLanguage(string requestedLangCode)
        {
            string stdOut;
            string stdErr;

            return GetBestSupportedLanguage(requestedLangCode, out stdOut, out stdErr);
        }

        // Determines which language code to use for eSpeak
        private string GetBestSupportedLanguage(
            string requestedLangCode,
            out string stdOut,
            out string stdErr
        )
        {
            stdOut = "";
            stdErr = "";

            // Normally requestedLangCode should be under our control.
            // But just do a quick and easy check to make sure it looks reasonable. (there are some highly contrived scenarios where a XSS injection would be possible with some social engineering.)
            if (requestedLangCode.Contains('"') || requestedLangCode.Contains('\\'))
            {
                // This doesn't look like a lang code and has non-zero potential for injection. just return a default value instead
                Debug.Assert(false);

                return "eo";
            }

            // First try the requested langauge directly.
            // (We need to test eSpeak directly instead of Aeneas because when using TTS overrides, there's no Aeneas error message that tells us if the language is unsupported.
            // Therefore, we explicitly test if the language is supported by the dependency (eSpeak) before getting started.
            if (
                !DoesCommandCauseError(
                    $"espeak -v {requestedLangCode} -q \"hello world\"",
                    kWorkingDirectory,
                    out stdOut,
                    out stdErr
                )
            )
            {
                return requestedLangCode;
            }

            // Nope, looks like the requested language is not supported by the eSpeak installation.
            // Let's check the fallback languages.

            var potentialFallbackLangs = new List<string>();

            // Check the orthography conversion files. If present, they specify the (first) fallback language to be used.
            string collectionPath = _bookSelection.CurrentSelection.CollectionSettings.FolderPath;
            var matchingFiles = Directory.EnumerateFiles(
                collectionPath,
                $"convert_{requestedLangCode}_to_*.txt"
            );
            foreach (var matchingFile in matchingFiles)
            {
                Tuple<string, string> sourceAndTargetTuple =
                    OrthographyConverter.ParseSourceAndTargetFromFilename(matchingFile);
                if (sourceAndTargetTuple != null)
                {
                    string targetLang = sourceAndTargetTuple.Item2;
                    potentialFallbackLangs.Add(targetLang);
                    break;
                }
            }

            // Add more default fallback languages to the end
            potentialFallbackLangs.Add("eo"); // "eo" is Esperanto
            potentialFallbackLangs.Add("en");

            // Now go and try the fallback languages until we (possibly) find one that works
            string langCode = null;
            foreach (var langCodeToTry in potentialFallbackLangs)
            {
                if (
                    !DoesCommandCauseError(
                        $"espeak -v {langCodeToTry} -q \"hello world\"",
                        kWorkingDirectory,
                        out stdOut,
                        out stdErr
                    )
                )
                {
                    langCode = langCodeToTry;
                    break;
                }
            }

            return langCode;
        }

        /// <summary>
        /// "Soft Split": Performs Forced Alignment and responds with the start times of each segment.
        /// Output format is a space-separated string of numbers representing the end time (calculated from the beginning of the file) in seconds.
        /// </summary>
        /// <param name="request"></param>
        public void GetForcedAlignmentTimings(ApiRequest request)
        {
            Logger.WriteEvent(
                "AudioSegmentationApi.GetForcedAlignmentTimings(): GetForcedAlignmentTimings started."
            );

            // Parse the JSON containing the text segmentation data.
            string json = request.RequiredPostJson();
            AutoSegmentRequest requestParameters = ParseJson(json);

            var response = GetAeneasTimings(requestParameters);
            if (response == null)
            {
                request.ReplyWithText("");
                return;
            }
            Logger.WriteEvent(
                "AudioSegmentationApi.GetForcedAlignmentTimings(): Completed successfully."
            );
            request.ReplyWithJson(response);
        }

        /// <summary>
        /// Given a directory and a filename base, finds the appropriate extension (if it exists) of a segmentable file.
        /// </summary>
        /// <returns>The file path (including directory) of a valid file if it exists, or null otherwise</returns>
        private string GetFileNameToSegment(string directoryName, string fileNameBase)
        {
            var extensions = new string[] { ".mp3", ".wav" };

            foreach (var extension in extensions)
            {
                string filePath = Path.Combine(directoryName, fileNameBase + extension);

                if (RobustFile.Exists(filePath))
                {
                    return filePath;
                }
            }

            return null;
        }

        /// <summary>
        /// Reads the orthography conversion settings file and applies the specified mapping to a list of strings
        /// </summary>
        /// <param name="fragments">The texts (as an IEnumerable<string>) to apply the mapping to</param>
        /// <param name="orthographyConversionFile">The filename containing the conversion settings. It should be tab-delimited with 2 columns. The 1st column is a sequence of 1 or more characters in the source language. The 2nd column contains a sequence of characteres to map to in the target language.</param>
        /// <returns></returns>
        public static IEnumerable<string> ApplyOrthographyConversion(
            IEnumerable<string> fragments,
            string orthographyConversionFile
        )
        {
            var converter = new OrthographyConverter(orthographyConversionFile);
            foreach (var fragment in fragments)
            {
                string mappedFragment = converter.ApplyMappings(fragment);
                yield return mappedFragment;
            }
        }

        /// <summary>
        /// Reads the orthography conversion settings file and applies the specified mapping to a piece of text
        /// </summary>
        /// <param name="text">The text (as a scalar string) to apply the mapping to</param>
        /// <param name="orthographyConversionFile">The filename containing the conversion settings. It should be tab-delimited with 2 columns. The 1st column is a sequence of 1 or more characters in the source language. The 2nd column contains a sequence of characteres to map to in the target language.</param>
        /// <returns></returns>
        public static string ApplyOrthographyConversion(
            string text,
            string orthographyConversionFile
        )
        {
            var converter = new OrthographyConverter(orthographyConversionFile);
            return converter.ApplyMappings(text);
        }

        public List<Tuple<string, string>> GetSplitStartEndTimings(
            string inputAudioFilename,
            string inputTextFragmentsFilename,
            string outputTimingsPath,
            string ttsEngineLang = "en"
        )
        {
            // Just setting some default value here (Esperanto - which is more phonetic so we think it works well for a large variety),
            // but really rely-ing on the TTS override to specify the real lang, so this value doesn't really matter.
            string aeneasLang = "eo";

            // I think this sets the boundary to the midpoint between the end of the previous sentence and the start of the next one.
            // This is good because by default, it would align it such that the subsequent audio started as close as possible to the beginning of it. Since there is a subtle pause when switching between two audio files, this left very little margin for error.
            string boundaryAdjustmentParams =
                "|task_adjust_boundary_algorithm=percent|task_adjust_boundary_percent_value=50";

            // This identifies a "head" region of between 0 seconds or up to the max-specified duration (e.g. 5 seconds or 12 seconds) of silence/non-intelligible.
            // This would prevent it from being included in the first sentence's audio. (FYI, the hidden format will suppress it from the output timings file).
            // Specify 0 to turn this off.
            string audioHeadParams =
                $"|os_task_file_head_tail_format=hidden|is_audio_file_detect_head_min=0.00|is_audio_file_detect_head_max={maxAudioHeadDurationSec.ToString(CultureInfo.InvariantCulture)}";
            var kTimingsOutputFormat = "txt";
            using (var tempFolder = new TemporaryFolder("Bloom-aeneas"))
            {
                // Note: The version of FFMPEG in output/Debug or output/Release is probably not compatible with the version required by Aeneas.
                // Therefore change the working path to a temporary folder in %TEMP%.  This has the benefit of allowing us to sidestep python's
                // inability to cope with non-ascii characters in file pathnames passed in on the command line by using bare filenames in the
                // command.  See https://issues.bloomlibrary.org/youtrack/issue/BL-6927.
                // Working in the %TEMP% folder also has the benefit of avoiding interference from file sync programs such as Dropbox, OneDrive,
                // etc.  See https://issues.bloomlibrary.org/youtrack/issue/BL-12801.
                var workingDirectory = tempFolder.FolderPath;
                var audioFile = Path.GetFileName(inputAudioFilename);
                RobustFile.Copy(
                    inputAudioFilename,
                    Path.Combine(workingDirectory, audioFile),
                    true
                );
                var fragmentsFile = Path.GetFileName(inputTextFragmentsFilename);
                RobustFile.Copy(
                    inputTextFragmentsFilename,
                    Path.Combine(workingDirectory, fragmentsFile),
                    true
                );
                var outputFile = Path.GetFileName(outputTimingsPath);

                // Have Aeneas output in its "txt" format, which is [f0001 start stop "label contents"] per line.
                // Later we will strip off the first field so that we have an Audacity-compatible label file ([start stop "label contents"]) that a user can edit
                string commandString =
                    $"python -m aeneas.tools.execute_task \"{audioFile}\" \"{fragmentsFile}\" \"task_language={aeneasLang}|is_text_type=plain|os_task_file_format={kTimingsOutputFormat}{audioHeadParams}{boundaryAdjustmentParams}\" \"{outputFile}\" --runtime-configuration=\"tts_voice_code={ttsEngineLang}\"";
                string command;
                string arguments;
                if (Platform.IsLinux)
                {
                    command = _pythonFound;
                    arguments = commandString.Substring(7);
                }
                else
                {
                    command = "CMD.EXE";
                    arguments = $"/C {commandString}";
                }

                var setPythonEncoding = SetPythonEncodingIfNeeded();
                Logger.WriteMinorEvent(
                    "AudioSegmentationApi.GetSplitStartEndTimings(): Command started, preparing to wait..."
                );
                var result = CommandLineRunnerExtra.RunWithInvariantCulture(
                    command,
                    arguments,
                    workingDirectory,
                    3600,
                    new NullProgress()
                );
                if (setPythonEncoding)
                    Environment.SetEnvironmentVariable(PythonIoEncodingKey, null);

                // Note: we could also request Aeneas write the standard output/error, or a log (or verbose log... or very verbose log) if desired
                if (result.ExitCode != 0)
                {
                    var stdout = result.StandardOutput;
                    var stderr = result.StandardError;
                    var sb = new StringBuilder();
                    sb.AppendLine(
                        $"ERROR: python aeneas process to segment the audio file finished with exit code = {result.ExitCode}."
                    );
                    sb.AppendLine($"working directory = {workingDirectory}");
                    sb.AppendLine($"failed command = {commandString}");
                    sb.AppendLine($"process.stdout = {stdout.Trim()}");
                    sb.AppendLine($"process.stderr = {stderr.Trim()}");
                    // Add information found during check for dependencies: it might be the wrong python or ffmpeg...
                    sb.AppendLine("--------");
                    sb.AppendLine($"python found = {_pythonFound}");
                    sb.AppendLine($"espeak found = {_espeakFound}");
                    sb.AppendLine($"ffmpeg found = {_ffmpegFound}");
                    sb.AppendLine($"aeneas information = {_aeneasInfo}");
                    sb.AppendLine("======== end of aeneas error report ========");
                    var msg = sb.ToString();
                    Console.Write(msg);
                    Logger.WriteEvent(msg);
                }
                else
                {
                    RobustFile.Copy(
                        Path.Combine(workingDirectory, outputFile),
                        outputTimingsPath,
                        true
                    );
                }
            }

            // This might throw exceptions, but IMO best to let the error handler pass it, and have the Javascript code be as robust as possible, instead of passing on error messages to user
            var segmentationResults = RobustFile.ReadAllLines(outputTimingsPath);

            List<Tuple<string, string>> timingStartEndRangeList;
            if (kTimingsOutputFormat.Equals("srt", StringComparison.OrdinalIgnoreCase))
            {
                timingStartEndRangeList = ParseTimingFileSRT(segmentationResults);
            }
            else if (kTimingsOutputFormat.Equals("txt", StringComparison.OrdinalIgnoreCase))
            {
                var startStopLabelList = ParseTimingLinesFromAeneasTXTFormat(segmentationResults);
                timingStartEndRangeList = startStopLabelList
                    .Select(x => new Tuple<string, string>(x.Item1, x.Item2))
                    .ToList();

                // That's all we need for now.  But next, we save it out in Audacity timings format in case the user wants to edit it
                var aeneasTimingsContents = startStopLabelList
                    .Select(x => $"{x.Item1}\t{x.Item2}\t{x.Item3}")
                    .ToList();
                // regardless of the input file extension, we want to use txt as the output because Audacity is extra painful if you are working with anything else, e.g. "tsv"
                var labelPath = Path.ChangeExtension(outputTimingsPath, ".txt");
                // convert that to a single string
                var aeneasTimingsContentsString = string.Join("\n", aeneasTimingsContents);
                RobustFile.WriteAllText(outputTimingsPath, aeneasTimingsContentsString);
            }
            else
            {
                timingStartEndRangeList = ParseTimingFileTSV(segmentationResults);
            }

            Logger.WriteMinorEvent(
                $"AudioSegmentationApi.GetSplitStartEndTimings(): Returning with count={timingStartEndRangeList.Count}."
            );
            return timingStartEndRangeList;
        }

        const string PythonIoEncodingKey = "PYTHONIOENCODING";

        /// <summary>
        /// Prevent python from complaining about unspecified encoding.  UTF-8 has to be what we want.
        /// </summary>
        private static bool SetPythonEncodingIfNeeded()
        {
            if (!Environment.GetEnvironmentVariables().Contains(PythonIoEncodingKey))
            {
                Environment.SetEnvironmentVariable(PythonIoEncodingKey, "UTF-8");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Parses the contents of a timing file and returns the start and end timing fields as a list of tuples.
        /// </summary>
        /// <param name="lines">The contents (line-by-line) of a .tsv timing file. Example:  f0001 1.000 4.980 "I have a dog"</param>
        public static List<Tuple<string, string, string>> ParseTimingLinesFromAeneasTXTFormat(
            IEnumerable<string> lines
        )
        {
            var timings = new List<Tuple<string, string, string>>();
            var lastEnd = "0.000";
            foreach (string line in lines)
            {
                // Each line in Aeneas TXT format is is "id start stop label], e.g.  'f0001 1.000 4.980 "I have a dog"'
                // split into four fields, with the last one being the label. The label is everything between the quotes.
                string[] fields = line.Split(new char[] { ' ' }, 4);

                if (fields.Length < 4 || !fields[3].StartsWith("\""))
                {
                    // so I don't know that Aeneas would ever fail to produce a valid line, but if it did, let's just
                    // output a line that sticks a label at a point in time after the last segment so that someone could
                    // fix it in Audacity or wherever. If this actually happens to someone, we might then look into
                    // it and see if we can do better.
                    timings.Add(Tuple.Create(lastEnd, lastEnd, $"Err:[{line}]"));
                }
                else
                {
                    // note we're skipping the first field, which is the id
                    string timingStart = fields[1].Trim();
                    string timingEnd = lastEnd = fields[2].Trim();

                    // make the label be the rest of the line
                    string label = string.Join(" ", fields.Skip(3)).Trim('"');

                    timings.Add(Tuple.Create(timingStart, timingEnd, label));
                }
            }

            return timings;
        }

        /// <summary>
        /// Parses the contents of a timing file and returns the start and end timing fields as a list of tuples.
        /// </summary>
        /// <param name="segmentationResults">The contents (line-by-line) of a .tsv timing file. Example: "1.000\t4.980\tf000001"</param>
        public static List<Tuple<string, string>> ParseTimingFileTSV(
            IEnumerable<string> segmentationResults
        )
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
                        timingStart = "0.000"; // Note: format generated by Aeneas seems independent of your Date/Time/Number Format settings.
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

            // Fix up any missing timingEnd values that we can trivially fix
            for (int i = 0; i < timings.Count - 1; ++i)
            {
                if (String.IsNullOrWhiteSpace(timings[i].Item2))
                {
                    var inferredTimingEnd = timings[i + 1].Item1; // Get the new timingEnd from the next timingStart
                    timings[i] = Tuple.Create(timings[i].Item1, inferredTimingEnd);
                }
            }

            return timings;
        }

        /// <summary>
        /// Parses the contents of a timing file and returns the start and end timing fields as a list of tuples.
        /// </summary>
        /// <param name="segmentationResults">The contents (line-by-line) of a .srt timing file</param>
        public static List<Tuple<string, string>> ParseTimingFileSRT(
            IList<string> segmentationResults
        )
        {
            var timings = new List<Tuple<string, string>>();

            // For now, just a simple parser that assumes the input is very well-formed, no attempt to re-align the states or anything
            // Each record comes in series of 4 lines. The first line has the fragment index (1-based), then the timing range, then the text, then a newline
            // We really only need the timing range for now so we just go straight to it and skip over everything else
            for (int lineNumber = 1; lineNumber <= segmentationResults.Count; lineNumber += 4)
            {
                string line = segmentationResults[lineNumber];
                string timingRange = line.Replace(',', '.'); // Convert from SRT's/Aeneas's HH:MM::SS,mmm format to FFMPEG's "HH:MM:SS.mmm" format. (aka European decimal points to American decimal points). This SRT format seems independent of system locale.
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

#if OBSOLETEAPI
        /// <summary>
        /// Given a list of timings, segments a whole audio file into the individual pieces specified by the timing list
        /// </summary>
        /// <param name="idList"></param>
        /// <param name="timingStartEndRangeList"></param>
        /// <param name="directoryName"></param>
        /// <param name="inputAudioFilename"></param>
        private void ExtractAudioSegments(
            IList<string> idList,
            IList<Tuple<string, string>> timingStartEndRangeList,
            string inputAudioFilename
        )
        {
            Debug.Assert(
                idList.Count == timingStartEndRangeList.Count,
                $"Number of text fragments ({idList.Count}) does not match number of extracted timings ({timingStartEndRangeList.Count}). The parsed timing ranges might be completely incorrect. The last parsed timing is: ({timingStartEndRangeList.Last()?.Item1 ?? "null"}, {timingStartEndRangeList.Last()?.Item2 ?? "null"})."
            );
            int size = Math.Min(timingStartEndRangeList.Count, idList.Count); // Note: it could differ if there is some discrepancy in line endings in the fragments file. This doesn't seem like it should happen but occasionally I see it.

            string extension = Path.GetExtension(inputAudioFilename); // Will include the "." e.g. ".mp3"
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".mp3";
            }

            Logger.WriteMinorEvent(
                $"AudioSegmentationApi.ExtractAudioSegments(): Starting off count={size} tasks in parallel."
            );

            // Allow each ffmpeg to run in parallel
            var tasksToWait = new Task[size];
            for (int i = 0; i < size; ++i)
            {
                var timingRange = timingStartEndRangeList[i];
                var timingStartString = timingRange.Item1;
                var timingEndString = timingRange.Item2;

                string splitFilename = $"{GetAudioDirectory()}/{idList[i]}{extension}";

                tasksToWait[i] = ExtractAudioSegmentAsync(
                    inputAudioFilename,
                    timingStartString,
                    timingEndString,
                    splitFilename
                );
            }

            // TODO: Need to report an error or exception or do a retry if a task fails.

            // Wait for them all so that the UI knows all the files are there before it starts mucking with the HTML structure.
            Task.WaitAll(tasksToWait.ToArray());
            Logger.WriteMinorEvent(
                $"AudioSegmentationApi.ExtractAudioSegments(): All tasks completed"
            );
        }

        /// <summary>
        /// Given a single timing, extract the specified segment of audio
        /// </summary>
        /// <param name="inputAudioFilename"></param>
        /// <param name="timingStartString"></param>
        /// <param name="timingEndString"></param>
        /// <param name="outputSplitFilename"></param>
        /// <returns></returns>
        private Task<int> ExtractAudioSegmentAsync(
            string inputAudioFilename,
            string timingStartString,
            string timingEndString,
            string outputSplitFilename
        )
        {
            // Since we are running this process aynchronously, we need to make sure that the process is not
            // affected by the current culture settings.  We can't use the CommandLineRunner methods.
            var currentCulture = CultureInfo.CurrentCulture;
            var currentUICulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
                string commandString =
                    $"ffmpeg -i \"{inputAudioFilename}\" -acodec copy -ss {timingStartString} -to {timingEndString} \"{outputSplitFilename}\"";
                string command;
                string arguments;
                var workingDir = Platform.IsLinux ? "/tmp" : kWorkingDirectory;
                if (Platform.IsLinux)
                {
                    command = _ffmpegFound;
                    arguments = commandString.Substring(7);
                }
                else
                {
                    command = "CMD.EXE";
                    arguments = $"/C {commandString}";
                }
                var startInfo = new ProcessStartInfo()
                {
                    FileName = command,
                    Arguments = arguments,
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                return RunProcessAsync(startInfo);
            }
            finally
            {
                CultureInfo.CurrentCulture = currentCulture;
                CultureInfo.CurrentUICulture = currentUICulture;
            }
        }

        // Starts a process and returns a task (that you can use to wait/await for the completion of the process)
        public static Task<int> RunProcessAsync(ProcessStartInfo startInfo)
        {
            var tcs = new TaskCompletionSource<int>();

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            process.Exited += (sender, args) =>
            {
                tcs.SetResult(process.ExitCode);
                process.Dispose();
            };

            process.Start();

            return tcs.Task;
        }
#endif
        #endregion


        #region ESpeak Preview

        internal class ESpeakPreviewResponse
        {
            public string text;
            public string lang;
            public string filePath;
        }

        /// <summary>
        /// API Handler when the Auto Segment button is clicked
        ///
        /// Replies with the text read (with orthography conversion applied) if eSpeak completed successfully, or "' if there was an error.
        /// </summary>
        public void ESpeakPreview(ApiRequest request)
        {
            Logger.WriteEvent("AudioSegmentationApi.ESpeakPreview(): ESpeakPreview started.");

            // Parse the JSON containing the text segmentation data.
            string json = request.RequiredPostJson();
            ESpeakPreviewRequest requestParameters =
                JsonConvert.DeserializeObject<ESpeakPreviewRequest>(json);

            string requestedLangCode = requestParameters.lang;
            string langCode = GetBestSupportedLanguage(requestedLangCode);
            Logger.WriteEvent(
                $"AudioSegmentationApi.ESpeakPreview(): langCode={langCode ?? "null"}"
            );

            string text = requestParameters.text;
            text = SanitizeTextForESpeakPreview(text);

            string collectionPath = _bookSelection.CurrentSelection.CollectionSettings.FolderPath;
            string orthographyConversionMappingPath = Path.Combine(
                collectionPath,
                $"convert_{requestedLangCode}_to_{langCode}.txt"
            );
            if (RobustFile.Exists(orthographyConversionMappingPath))
            {
                text = ApplyOrthographyConversion(text, orthographyConversionMappingPath);
            }

            // Even though you theoretically can pass the text through on the command line, it's better to write it to file.
            // The way the command line handles non-ASCII characters is not guaranteed to be the same as when reading from file.
            // It's more straightforward to just read/write it from file.
            // This causes the audio to match the audio that Aeneas will hear when it calls its eSpeak dependency.
            //   (Well, actually it was difficult to verify the exact audio that Aeneas hears, but for -v el "άλφα", verified reading from file caused audio duration to match, but passing on command line caused discrepancy in audio duration)
            string textToSpeakFullPath = Path.GetTempFileName();
            RobustFile.WriteAllText(textToSpeakFullPath, text, Encoding.UTF8);

            // No need to wait for espeak before responding.
            var response = new ESpeakPreviewResponse()
            {
                text = text,
                lang = langCode,
                filePath = RobustFile.Exists(orthographyConversionMappingPath)
                    ? Path.GetFileName(orthographyConversionMappingPath)
                    : ""
            };
            string responseJson = JsonConvert.SerializeObject(response);
            request.ReplyWithJson(responseJson);

            string stdout;
            string command = $"espeak -v {langCode} -f \"{textToSpeakFullPath}\"";
            bool success = !DoesCommandCauseError(command, out stdout);
            RobustFile.Delete(textToSpeakFullPath);
            Logger.WriteEvent(
                "AudioSegmentationApi.ESpeakPreview() Completed with success = " + success
            );
            if (!success)
            {
                var message = L10NSharp.LocalizationManager.GetString(
                    "EditTab.Toolbox.TalkingBookTool.ESpeakPreview.Error",
                    "eSpeak failed.",
                    "This text is shown if an error occurred while running eSpeak. eSpeak is a piece of software that this program uses to do text-to-speech (have the computer read text out loud)."
                );
                NonFatalProblem.Report(ModalIf.None, PassiveIf.All, message, null, null, false); // toast without allowing error report.
            }
        }

        // Clean up the text before passing it off to the command line
        public static string SanitizeTextForESpeakPreview(string unsafeText)
        {
            // Prevent the string from being prematurely terminated and allowing a malicious user to inject code
            // Here is an example you can type into a text box to test with:
            //   Hello world" && espeak -v en "If you did not hear it say and and, then there is an XSS vulnerability."
            unsafeText = unsafeText.Replace('"', ' '); // Get rid of quotes so that they can't mess up the command line quotes. Escaping the quotes with a backslash is more complicated and a single level of escaping didn't seem to fix it either. (And even levels won't help because it's now just escaping itself).

            // Backslash cases don't seem to be a problem. Probably means it's escaped automatically?
            // Here are some cases you can use to test with:
            //   You should be about to hear backslash alpha. \alpha
            //   You should be about to hear backslash backslash alpha. \\alpha

            // Handle text boxes with multiple paragraphs
            unsafeText = unsafeText.Replace('\n', ' ');
            unsafeText = unsafeText.Replace('\r', ' ');

            string sanitizedText = unsafeText;
            return sanitizedText;
        }

        #endregion
    }
}
