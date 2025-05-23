using System;
using System.Diagnostics;
using Newtonsoft.Json;
using System.IO;
using SIL.IO;
using SIL.Reporting;
using Bloom.Utils;
using System.Dynamic;
using System.Collections.Generic;

namespace Bloom.Book
{
    public class UserPrefs
    {
        private bool _loading = true;
        private string _filePath;
        private int _mostRecentPage;
        private bool _reducePdfMemory;
        private string _colorProfileForPdf;
        private bool _fullBleed;
        private string _spreadsheetFolder;
        private bool _uploadAgreementsAccepted;

        private UserPrefs() { }

        public static UserPrefs LoadOrMakeNew(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return null;

            UserPrefs userPrefs = null;
            if (RobustFile.Exists(fileName))
            {
                try
                {
                    userPrefs = JsonConvert.DeserializeObject<UserPrefs>(
                        RobustFile.ReadAllText(fileName)
                    );
                    if (userPrefs == null)
                        throw new ApplicationException(
                            "JsonConvert.DeserializeObject() returned null"
                        );
                    if (userPrefs.ColorProfileForPdf == null)
                    {
                        // This is a workaround for the fact that we used to have a property called cmykPdf
                        // which was a boolean. Now we have a string property called ColorProfileForPdf.
                        // We need to convert the old value to the new one, without preserving the old value.
                        // The new value will be null if it has never been set, otherwise it may be the
                        // empty string.
                        var oldPrefs = JsonConvert.DeserializeObject<ExpandoObject>(RobustFile.ReadAllText(fileName)) as IDictionary<string, object>;
                        if (oldPrefs != null && oldPrefs.TryGetValue("cmykPdf", out var cmykPdf))
                        {
                            if (cmykPdf is bool && (bool)cmykPdf)
                                userPrefs.ColorProfileForPdf = "USWebCoatedSWOP";
                            else if (cmykPdf is string && (string)cmykPdf == "true")
                                userPrefs.ColorProfileForPdf = "USWebCoatedSWOP";
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.WriteEvent("error reading UserPrefs at " + fileName + "  " + e.Message);
                    //otherwise, just give them a new user prefs
                    userPrefs = null;
                }
            }
            if (userPrefs == null)
                userPrefs = new UserPrefs();
            userPrefs._filePath = fileName;
            userPrefs._loading = false;
            return userPrefs;
        }

        /// <summary>
        /// Used when the directory is changed because the book has been renamed
        /// </summary>
        /// <param name="newDirectoryName"></param>
        public void UpdateFileLocation(string newDirectoryName)
        {
            _filePath = Path.Combine(newDirectoryName, Path.GetFileName(_filePath));
        }

        [JsonProperty("mostRecentPage")]
        public int MostRecentPage
        {
            get { return _mostRecentPage; }
            set
            {
                _mostRecentPage = value;
                Save();
            }
        }

        [JsonProperty("reducePdfMemory")]
        public bool ReducePdfMemoryUse
        {
            get { return _reducePdfMemory; }
            set
            {
                _reducePdfMemory = value;
                Save();
            }
        }

        [JsonProperty("colorProfileForPdf")]
        public string ColorProfileForPdf
        {
            get { return _colorProfileForPdf; }
            set
            {
                _colorProfileForPdf = value;
                Save();
            }
        }

        [JsonProperty("fullBleed")]
        public bool FullBleed
        {
            get { return _fullBleed; }
            set
            {
                _fullBleed = value;
                Save();
            }
        }

        // The folder where this book was last written as a spreadsheet or from which it was last imported.
        // Null for books where either has never happened. Be careful...this might be a path to a folder
        // that does not exist (e.g., deleted by user, or on a thumb drive since dismounted).
        // If so, it should be ignored.
        [JsonProperty("spreadsheetFolder")]
        public string SpreadsheetFolder
        {
            get { return _spreadsheetFolder; }
            set
            {
                _spreadsheetFolder = value;
                Save();
            }
        }

        [JsonProperty("uploadAgreementsAccepted")]
        public bool UploadAgreementsAccepted
        {
            get { return _uploadAgreementsAccepted; }
            set
            {
                _uploadAgreementsAccepted = value;
                Save();
            }
        }

        private void Save()
        {
            // We're checking this because the deserialization routine calls the property setters which triggers a save. We don't
            // want to save while loading.
            if (_loading)
                return;
            var prefs = JsonConvert.SerializeObject(this);

            Debug.Assert(!string.IsNullOrWhiteSpace(prefs));

            try
            {
                if (!string.IsNullOrWhiteSpace(prefs))
                {
                    var temp = new TempFileForSafeWriting(_filePath);
                    try
                    {
                        RobustFile.WriteAllText(temp.TempFilePath, prefs);
                    }
                    catch (UnauthorizedAccessException error)
                    {
                        throw new BloomUnauthorizedAccessException(temp.TempFilePath, error);
                    }

                    // This can fail if there isn't permission to write to the book folder.
                    try
                    {
                        temp.WriteWasSuccessful();
                    }
                    catch (UnauthorizedAccessException error)
                    {
                        throw new BloomUnauthorizedAccessException(_filePath, error);
                    }
                }
            }
            catch (Exception error)
            {
                //For https://silbloom.myjetbrains.com/youtrack/issue/BL-3222  we did a real fix for 3.6.
                //But this will cover us for future errors here, which are not worth stopping the user from doing work.
                NonFatalProblem.Report(
                    ModalIf.Alpha,
                    PassiveIf.All,
                    "Problem saving book preferences",
                    "book.userprefs could not be saved to " + _filePath,
                    error
                );
            }
        }
    }
}
