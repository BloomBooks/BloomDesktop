using System;
using System.IO;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.CollectionTab;
using Bloom.MiscUI;
using Bloom.Properties;
using Bloom.ToPalaso;
using L10NSharp;
using SIL.IO;

namespace Bloom.Spreadsheet
{
    /// <summary>
    /// Commands that affect entire books, typically menu commands in the Collection tab right-click menu
    /// </summary>
    public class SpreadsheetApi
    {
        private readonly CollectionModel _collectionModel;
        private BookSelection _bookSelection;
        private readonly BloomWebSocketServer _webSocketServer;

        public SpreadsheetApi(
            CollectionModel collectionModel,
            BloomWebSocketServer webSocketServer,
            BookSelection bookSelection
        )
        {
            _collectionModel = collectionModel;
            _webSocketServer = webSocketServer;
            _bookSelection = bookSelection;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // Note: all book commands, including import and export spreadsheets,
            // go through a single "bookCommand/" API, so we don't register that here.
            // Instead all we need to register is any api enpoints used by our own spreadsheet dialogs

            apiHandler.RegisterEndpointHandler("spreadsheet/export", ExportToSpreadsheet, true);
        }

        public void ShowExportToSpreadsheetUI(Book.Book book)
        {
            // Throw up a Requires Bloom Enterprise dialog if it's not turned on
            if (!_collectionModel.CollectionSettings.HaveEnterpriseFeatures)
            {
                Enterprise.ShowRequiresEnterpriseNotice(Form.ActiveForm, "Export to Spreadsheet");
                return;
            }

            dynamic messageBundle = new DynamicJson();
            messageBundle.folderPath = GetSpreadsheetFolderFor(book, true);
            _webSocketServer.LaunchDialog("SpreadsheetExportDialog", messageBundle);
        }

        private void ExportToSpreadsheet(ApiRequest request)
        {
            var book = _bookSelection.CurrentSelection;
            var bookPath = book.GetPathHtmlFile();
            try
            {
                var dom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtmlFile(bookPath, false));
                var exporter = new SpreadsheetExporter(_webSocketServer, book.CollectionSettings);
                string outputParentFolder = request.RequiredPostDynamic().parentFolderPath;
                string outputFolder = Path.Combine(
                    outputParentFolder,
                    Path.GetFileNameWithoutExtension(bookPath)
                );
                SetSpreadsheetFolder(book, outputFolder);
                string imagesFolderPath = Path.GetDirectoryName(bookPath);

                exporter.ExportToFolderWithProgressAsync(
                    dom,
                    imagesFolderPath,
                    outputFolder,
                    outputFilePath =>
                    {
                        if (outputFilePath != null)
                            ProcessExtra.SafeStartInFront(outputFilePath);
                    }
                );
            }
            catch (Exception ex)
            {
                var msg = LocalizationManager.GetString(
                    "Spreadsheet:ExportFailed",
                    "Export failed: "
                );
                NonFatalProblem.Report(
                    ModalIf.All,
                    PassiveIf.None,
                    msg + ex.Message,
                    exception: ex
                );
            }
            request.PostSucceeded();
        }

        /// <summary>
        /// This is our primary entry point for importing from a spreadsheet. There is also a CLI command,
        /// but we shouldn't need that one much, now that we have this on our book thumb context menus.
        /// </summary>
        public void HandleImportContentFromSpreadsheet(Book.Book book)
        {
            if (!_collectionModel.CollectionSettings.HaveEnterpriseFeatures)
            {
                Enterprise.ShowRequiresEnterpriseNotice(Form.ActiveForm, "Import to Spreadsheet");
                return;
            }

            var bookPath = book.GetPathHtmlFile();
            try
            {
                string inputFilepath;
                using (var dlg = new BloomOpenFileDialog())
                {
                    dlg.Filter = "xlsx|*.xlsx";
                    dlg.RestoreDirectory = true;
                    dlg.InitialDirectory = GetSpreadsheetFolderFor(book, false);
                    if (DialogResult.Cancel == dlg.ShowDialog())
                    {
                        return;
                    }
                    inputFilepath = dlg.FileName;
                    var spreadsheetFolder = Path.GetDirectoryName(inputFilepath);
                    SetSpreadsheetFolder(book, spreadsheetFolder);
                }

                var importer = new SpreadsheetImporter(
                    _webSocketServer,
                    book,
                    Path.GetDirectoryName(inputFilepath)
                );
                importer.ControlForInvoke = Shell.GetShellOrOtherOpenForm();
                importer.ImportWithProgressAsync(
                    inputFilepath,
                    () =>
                    {
                        // The importer now does BringBookUpToDate() which accomplishes everything this did,
                        // plus it may have actually changed the folder (and subsequent 'bookPath')
                        // due to a newly imported title. That would cause this call to fail.
                        //XmlHtmlConverter.SaveDOMAsHtml5(book.OurHtmlDom.RawDom, bookPath);
                        book.ReloadFromDisk(null);
                        _bookSelection.InvokeSelectionChanged(false);
                    }
                );
            }
            catch (Exception ex)
            {
                var msg = LocalizationManager.GetString(
                    "Spreadsheet:ImportFailed",
                    "Import failed: "
                );
                NonFatalProblem.Report(
                    ModalIf.All,
                    PassiveIf.None,
                    msg + ex.Message,
                    exception: ex,
                    showSendReport: false
                );
            }
        }

        /// <summary>
        /// Get and set the folder where we should initially open the chooser for importing (or exporting, if
        /// forExport is true) the book as a spreadsheet.
        /// </summary>
        private string GetSpreadsheetFolderFor(Book.Book book, bool forExport)
        {
            var folder = book.UserPrefs.SpreadsheetFolder; // saved directory if any for this book
            if (!Directory.Exists(folder))
            {
                // Fall back to the last place used for ANY import or export, or if there is no such, the system Documents folder.
                return String.IsNullOrWhiteSpace(Settings.Default.ExportImportFileFolder)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                    : Settings.Default.ExportImportFileFolder;
            }

            if (forExport)
            {
                // We have a saved location (the folder of the spreadsheet itself), but for export we want its parent
                // (the folder in which we will create a folder for the spreadsheet)
                return Path.GetDirectoryName(folder);
            }

            // Prevent previous saved values from allowing us to store in the collection's folder. (BL-11188)
            if (folder.StartsWith(book.Storage.FolderPath, StringComparison.InvariantCulture))
                return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            return folder; // save folder itself for import
        }

        private void SetSpreadsheetFolder(Book.Book book, string folder)
        {
            book.UserPrefs.SpreadsheetFolder = folder;
            // We go up a level since the input folder is specific to this book, while the parent of that
            // is a likely place to do future exports and imports of books that have not previously
            // been imported or exported.
            Settings.Default.ExportImportFileFolder = Path.GetDirectoryName(folder);
            Settings.Default.Save();
        }
    }
}
