using System;

namespace Bloom.Utils
{
    public static class OneDriveUtils
    {
        private static string[] oneDriveErrorCodes =
        {
            "80070184", // This one is not in Microsoft's list. I got it by disconnecting my internet.
            // The rest are from https://support.microsoft.com/en-au/office/what-do-the-onedrive-error-codes-mean-f7a68338-e540-4ebf-ad5d-56c5633acded#ID0EBBH=Error_codes
            "80010007",
            "80040c81",
            "8004de80",
            "8004de86",
            "8004de85",
            "8004de8a",
            "8004de90",
            "8004de96",
            "8004ded7",
            "8004dedc",
            "8004def0",
            "8004def7",
            "80070005",
            "8007016a",
            "80070194",
            "80071128",
            "80071129"
        };

        public static bool isOneDriveErrorCode(string errorCode)
        {
            return oneDriveErrorCodes.Contains(errorCode.ToLowerInvariant());
        }

        public static string getOneDriveErrorDialogMessage()
        {
            return L10NSharp.LocalizationManager.GetString(
                    "ReportProblemDialog.OneDriveErrorMessage",
                    "There is a problem with your Microsoft OneDrive which is preventing Bloom from accessing files."
                ) + Environment.NewLine; // TODO I thought I got rid of this...
        }

        public static string getOneDriveErrorDialogHeader()
        {
            return L10NSharp.LocalizationManager.GetString(
                "ReportProblemDialog.OneDriveProblem",
                "OneDrive Problem"
            );
        }
    }
}
