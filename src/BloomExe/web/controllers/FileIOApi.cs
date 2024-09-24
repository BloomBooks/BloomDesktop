using Bloom.Api;
using Bloom.Book;
using Bloom.Publish;
using Bloom.ToPalaso;
using Bloom.Utils;
using Newtonsoft.Json;
using SIL.IO;
using System;
using System.Diagnostics;
using SIL.PlatformUtilities;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using System.IO;
using System.Threading;

namespace Bloom.web.controllers
{
    class FileTypeForFileDialog
    {
        public string name;
        public string[] extensions;
    }

    class OpenFileRequest
    {
        public string title;
        public FileTypeForFileDialog[] fileTypes;
        public string defaultPath;

        // If specified, the file will be copied to this sub folder in the book folder, and just the name returned.
        public string destFolder;
    }

    public class FileIOApi
    {
        private readonly BookSelection _bookSelection;
        private readonly BloomWebSocketServer _webSocketServer;

        // The current book we are editing
        private Book.Book CurrentBook => _bookSelection.CurrentSelection;

        // Called by Autofac, which creates the one instance and registers it with the server.
        public FileIOApi(BookSelection bookSelection, BloomWebSocketServer webSocketServer)
        {
            _bookSelection = bookSelection;
            _webSocketServer = webSocketServer;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler("fileIO/openFileInDefaultEditor", OpenFile, true);
            apiHandler.RegisterEndpointHandler("fileIO/chooseFile", ChooseFile, true);
            apiHandler.RegisterEndpointHandler(
                "fileIO/getSpecialLocation",
                GetSpecialLocation,
                true
            );
            apiHandler.RegisterEndpointHandler("fileIO/copyFile", CopyFile, true);
            apiHandler.RegisterEndpointHandler("fileIO/chooseFolder", HandleChooseFolder, true);
            apiHandler.RegisterEndpointHandler(
                "fileIO/showInFolder",
                HandleShowInFolderRequest,
                true
            ); // Common

            apiHandler.RegisterEndpointHandler("fileIO/listFiles", HandleListFiles, true);
        }

        // Return a list of files in the specified subfolder of the browser root.
        // param subPath: the subfolder to list files in
        // param match: optional, a pattern to match files against (in the style of Directory.GetFiles)
        private void HandleListFiles(ApiRequest request)
        {
            var subPath = request.RequiredParam("subPath");
            var match = request.GetParamOrNull("match");
            var files = Directory.GetFiles(
                Path.Combine(
                    FileLocationUtilities.DirectoryOfApplicationOrSolution,
                    BloomFileLocator.BrowserRoot,
                    subPath
                ),
                match ?? "*",
                SearchOption.TopDirectoryOnly
            );
            var result = new { files = files.Select(f => Path.GetFileName(f)).ToArray() };
            request.ReplyWithJson(JsonConvert.SerializeObject(result));
        }

        private void ChooseFile(ApiRequest request)
        {
            lock (request)
            {
                string json = request.RequiredPostJson();
                OpenFileRequest requestParameters = JsonConvert.DeserializeObject<OpenFileRequest>(
                    json
                );

                request.ReplyWithText(SelectFileUsingDialog(requestParameters));
            }
        }

        private string SelectFileUsingDialog(OpenFileRequest requestParameters)
        {
            var initialDirectory = string.IsNullOrEmpty(requestParameters.defaultPath)
                ? System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments)
                : Path.GetDirectoryName(requestParameters.defaultPath); // enhance would be better to actually select the file, not just the Dir?
            using (var dlg = new MiscUI.BloomOpenFileDialog
            {
                Title = requestParameters.title,
                InitialDirectory = initialDirectory,
                FileName = Path.GetFileName(requestParameters.defaultPath),
                Filter = string.Join(
                    "|",
                    requestParameters.fileTypes.Select(
                        fileType =>
                            $"{fileType.name}|{string.Join(";", fileType.extensions.Select(e => "*." + e))}"
                    )
                ),
            })
            {
                var result = dlg.ShowDialog();
                if (result == DialogResult.OK)
                {
                    // We are not trying get a memory or time diff, just a point measure.
                    PerformanceMeasurement.Global.Measure("Choose file", dlg.FileName)?.Dispose();

                    if (!string.IsNullOrEmpty(requestParameters.destFolder))
                    {
                        var fileName = Path.GetFileNameWithoutExtension(dlg.FileName);
                        var ext = Path.GetExtension(dlg.FileName);
                        var destFolder = Path.Combine(
                            CurrentBook.FolderPath,
                            requestParameters.destFolder
                        );
                        var destPath = BookStorage.GetUniqueFileName(destFolder, fileName, ext);
                        Directory.CreateDirectory(destFolder);
                        RobustFile.Copy(dlg.FileName, destPath);
                        return Path.GetFileName(destPath);
                    }

                    return dlg.FileName.Replace("\\", "/");
                }
            }

            return String.Empty;
        }

        private void GetSpecialLocation(ApiRequest request)
        {
            lock (request)
            {
                switch (request.RequiredPostEnumAsJson<SpecialLocation>())
                {
                    case SpecialLocation.CurrentBookAudioDirectory:
                        var currentBookAudioDirectoryPath = AudioProcessor.GetAudioFolderPath(
                            CurrentBook.FolderPath
                        );
                        Directory.CreateDirectory(currentBookAudioDirectoryPath);
                        request.ReplyWithText(currentBookAudioDirectoryPath.Replace("\\", "/"));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private void OpenFile(ApiRequest request)
        {
            dynamic jsonData;
            try
            {
                jsonData = DynamicJson.Parse(request.RequiredPostJson());
            }
            catch (Exception e)
            {
                request.Failed(HttpStatusCode.BadRequest, $"BadRequest: {e.ToString()}");
                return;
            }

            try
            {
                ProcessExtra.SafeStartInFront(jsonData.path);
            }
            catch (Exception e)
            {
                request.Failed(
                    HttpStatusCode.InternalServerError,
                    "InternalServerError while trying to open file. " + e.ToString()
                );
                return;
            }

            request.PostSucceeded();
        }

        private void CopyFile(ApiRequest request)
        {
            dynamic jsonData;
            try
            {
                jsonData = DynamicJson.Parse(request.RequiredPostJson());
            }
            catch (Exception e)
            {
                request.Failed(HttpStatusCode.BadRequest, $"BadRequest: {e.ToString()}");
                return;
            }

            try
            {
                var source = jsonData.from;
                if (
                    !Path.IsPathRooted(source)
                    && (
                        Path.GetExtension(source).ToLowerInvariant() == ".mp3"
                        || Path.GetExtension(source).ToLowerInvariant() == ".webm"
                    )
                )
                {
                    // MP3 files in the sounds folder can be found automatically.
                    // We can extend this as necessary if there are other built-in files
                    // we want to be easily able to copy like this.
                    source = Path.Combine(
                        FileLocationUtilities.DirectoryOfApplicationOrSolution,
                        BloomFileLocator.BrowserRoot,
                        "sounds",
                        source
                    );
                }
                RobustFile.Copy(source, jsonData.to, true);
            }
            catch (Exception e)
            {
                request.Failed(
                    HttpStatusCode.InternalServerError,
                    "InternalServerError while copying file. " + e.ToString()
                );
                return;
            }

            request.PostSucceeded();
        }

        public void HandleChooseFolder(ApiRequest request)
        {
            var initialPath = request.GetParamOrNull("path");
            var description = request.GetParamOrNull("description");
            var forOutput = request.GetParamOrNull("forOutput");
            var isForOutput =
                !String.IsNullOrEmpty(forOutput) && forOutput.ToLowerInvariant() == "true";

            var resultPath = Utils.MiscUtils.GetOutputFolderOutsideCollectionFolder(
                initialPath,
                description,
                isForOutput
            );

            dynamic result = new DynamicJson();
            result.success = !String.IsNullOrEmpty(resultPath);
            result.path = resultPath;

            // We send the result through a websocket rather than simply returning it because
            // if the user is very slow (one site said FF times out after 90s) the browser may
            // abandon the request before it completes. The POST result is ignored and the
            // browser simply listens to the socket.
            // We'd prefer this request to return immediately and set a callback to run
            // when the dialog closes and handle the results, but FolderBrowserDialog
            // does not offer such an API. Instead, we just ignore any timeout
            // in our Javascript code.


            _webSocketServer.SendBundle("fileIO", "chooseFolder-results", result);
            request.PostSucceeded();
        }

        // Request from javascript to open the folder containing the specified file,
        // and select it.
        // Currently we are assuming the path is relative to the book directory,
        // since typically paths JS has access to only go that far up.
        private void HandleShowInFolderRequest(ApiRequest request)
        {
            lock (request)
            {
                var requestData = DynamicJson.Parse(request.RequiredPostJson());
                string partialFolderPath = requestData.folderPath;
                string folderPath = partialFolderPath;
                if (!Path.IsPathRooted(partialFolderPath))
                    folderPath = Path.Combine(
                        _bookSelection.CurrentSelection.FolderPath,
                        partialFolderPath
                    );
                SelectFileInExplorer(folderPath);
                // It may or may not have succeeded but nothing in JS wants to know it didn't, and hiding
                // the failure there is a nuisance.

                request.PostSucceeded();
            }
        }

        /// <summary>
        /// Open the folder containing the specified file and select it.
        /// </summary>
        /// <param name="filePath"></param>
        private static void SelectFileInExplorer(string filePath)
        {
            try
            {
                ToPalaso.ProcessExtra.ShowFileInExplorerInFront(
                    filePath.Replace("/", Path.DirectorySeparatorChar.ToString())
                );
            }
            catch (System.Runtime.InteropServices.COMException e)
            {
                SIL.Reporting.ErrorReport.NotifyUserOfProblem(
                    e,
                    $"Bloom had a problem asking your operating system to show {filePath}. Sorry!"
                );
            }
            var folderName = Path.GetFileName(Path.GetDirectoryName(filePath));
            BringFolderToFrontInLinux(folderName);
        }

        /// <summary>
        /// Make sure the specified folder (typically one we just opened an explorer on)
        /// is brought to the front in Linux (BL-673). This is automatic in Windows.
        /// </summary>
        /// <param name="folderName"></param>
        public static void BringFolderToFrontInLinux(string folderName)
        {
            if (Platform.IsLinux)
            {
                // allow the external process to execute
                Thread.Sleep(100);

                // if the system has wmctrl installed, use it to bring the folder to the front
                // This process is not affected by the current culture, so we don't need to adjust it.
                // We don't wait for this to finish, so we don't use the CommandLineRunner methods.
                Process.Start(
                    new ProcessStartInfo()
                    {
                        FileName = "wmctrl",
                        Arguments = "-a \"" + folderName + "\"",
                        UseShellExecute = false,
                        ErrorDialog = false // do not show a message if not successful
                    }
                );
            }
        }
    }

    enum SpecialLocation
    {
        CurrentBookAudioDirectory
    }
}
