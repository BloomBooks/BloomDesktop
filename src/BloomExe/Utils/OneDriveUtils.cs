using System;
using System.Linq;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.ErrorReporter;
using Bloom.web.controllers;
using SIL.Reporting;

namespace Bloom.Utils
{
    public static class OneDriveUtils
    {
        private static string[] oneDriveErrorCodes =
        {
            "80070184", // This one is not in Microsoft's list. I got it by disconnecting my internet.
            // The rest are from https://support.microsoft.com/en-au/office/what-do-the-onedrive-error-codes-mean-f7a68338-e540-4ebf-ad5d-56c5633acded#ID0EBBH=Error_codes
            "80040c81",
            "8004de80",
            "8004de86",
            "8004de85",
            "8004de8a",
            "8004de90",
            "8004ded7",
            "8004dedc",
            "8004def0",
            "8004def7",
            "8007016a",
            "80070194",
            "80071129",
        };

        public static bool IsOneDriveErrorCode(string errorCode)
        {
            return oneDriveErrorCodes.Contains(errorCode.ToLowerInvariant());
        }

        public static string GetOneDriveErrorDialogMessage()
        {
            return L10NSharp.LocalizationManager.GetString(
                "ReportProblemDialog.OneDriveErrorMessage",
                "There is a problem with your Microsoft OneDrive which is preventing Bloom from accessing files."
            );
        }

        public static string GetOneDriveErrorDialogHeader()
        {
            return L10NSharp.LocalizationManager.GetString(
                "ReportProblemDialog.OneDriveProblem",
                "OneDrive Problem"
            );
        }

        /// <summary>
        /// We want to show different UI for exceptions caused by the user's OneDrive, which we cannot do anything about
        /// And should inform the user about. See BL-12977
        /// Recursively checks inner exceptions.
        /// </summary>
        /// <param name="error">The exception to check. We will check its inner exceptions also.</param>
        /// <param name="filePath">Path of file that we were trying to access, causing this error. To display to the user</param>
        /// <param name="levelOfProblem">"fatal" or "nonfatal", for UI display</param>
        public static bool CheckForAndHandleOneDriveExceptions(
            System.Exception error,
            string filePath = "",
            string levelOfProblem = "nonfatal"
        )
        {
            if (error == null)
                return false;

            string fileExceptionFilePath = FileException.GetFilePathIfPresent(error);
            if (!string.IsNullOrEmpty(fileExceptionFilePath))
            {
                filePath = fileExceptionFilePath;
            }
            // FileException is a Bloom exception to capture the filepath. We want to report the inner, original exception.
            Exception originalError = FileException.UnwrapIfFileException(error);

            string errorCode = originalError.HResult.ToString("X");

            if (!IsOneDriveErrorCode(errorCode))
            {
                // This exception is not one of the OneDrive exception codes we check for,
                // but check if it has an inner exception that is
                if (originalError?.InnerException != null)
                {
                    return CheckForAndHandleOneDriveExceptions(
                        originalError.InnerException,
                        filePath
                    );
                }
                else
                {
                    // this is not a special error code, return to normal error handling
                    return false;
                }
            }

            Control control = Shell.GetShellOrOtherOpenForm();
            if (control == null) // still possible if we come from a "Details" button
                control = FatalExceptionHandler.ControlOnUIThread;

            string logPath = Logger.LogPath.Replace('\\', '/');

            string errorMessageLabel = "Error Message";
            string errorCodeLabel = "Error Number";
            string filePathLabel = "File";
            string logPathLabel = "Bloom Log";
            void ShowOneDriveExceptionFallbackDialog(
                Exception error,
                string errorCode,
                string filePath,
                string logPath
            )
            {
                // We decided not to localize this for now.
                MessageBox.Show(
                    GetOneDriveErrorDialogMessage()
                        + Environment.NewLine
                        + Environment.NewLine
                        + $"{errorMessageLabel}: {error.Message}{Environment.NewLine}"
                        + $"{errorCodeLabel}: {errorCode}{Environment.NewLine}"
                        + $"{filePathLabel}: {filePath}{Environment.NewLine}"
                        + $"{logPathLabel}: {logPath}",
                    GetOneDriveErrorDialogHeader(),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }

            if (BloomServer._theOneInstance == null)
            {
                // We got an error really early, before we can use HTML dialogs
                ShowOneDriveExceptionFallbackDialog(originalError, errorCode, filePath, logPath);

                return true;
            }
            var showOneDriveFallbackDialogAction = new Action(() =>
            {
                ShowOneDriveExceptionFallbackDialog(originalError, errorCode, filePath, logPath);
            });

            string searchOnline = L10NSharp.LocalizationManager.GetString(
                "ReportProblemDialog.SearchOnline",
                "Search online"
            );
            string searchOnlineLink =
                errorCode == "80070184"
                    ? "" // as of now, error "80070184" does not have good online microsoft support results
                    : $"<a href=\"https://support.microsoft.com/en-us/search?query=0x{errorCode}\">{searchOnline}</a><br>";

            var reactDialogProps = new
            {
                level = ProblemLevel.kNotify, // Always use the notify dialog (no report button) for OneDrive errors
                message = GetOneDriveErrorDialogMessage(),
                detailsBoxText = $"Error message<br>{originalError.Message}<br>Error number<br>"
                    + $"0x{errorCode} {searchOnlineLink}"
                    + (!string.IsNullOrEmpty(filePath) ? $"File<br>{filePath}<br>" : "")
                    + $"<a href=\"file:///{logPath}\">Bloom Log</a><br>",
                titleOverride = GetOneDriveErrorDialogHeader(),
                titleL10nKeyOverride = "ReportProblemDialog.OneDriveProblem",
                themeOverride = levelOfProblem,
            };

            ProblemReportApi.ShowProblemReactDialogWithFallbacks(
                showOneDriveFallbackDialogAction,
                reactDialogProps,
                GetOneDriveErrorDialogHeader(),
                null,
                control,
                originalError,
                450
            );

            return true;
        }
    }
}
