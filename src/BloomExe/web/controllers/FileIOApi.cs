using System;
using System.IO;
using System.Net;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Publish;
using Bloom.Utils;
using SIL.IO;

namespace Bloom.web.controllers
{
	public class FileIOApi
	{
		private readonly BookSelection _bookSelection;

		// The current book we are editing
		private Book.Book CurrentBook => _bookSelection.CurrentSelection;

		// Called by Autofac, which creates the one instance and registers it with the server.
		public FileIOApi(BookSelection bookSelection)
		{
			_bookSelection = bookSelection;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("fileIO/chooseFile", ChooseFile, true);
			apiHandler.RegisterEndpointHandler("fileIO/getSpecialLocation", GetSpecialLocation, true);
			apiHandler.RegisterEndpointHandler("fileIO/copyFile", CopyFile, true);
		}

		private void ChooseFile(ApiRequest request)
		{
			lock (request)
				request.ReplyWithText(SelectFileUsingDialog());
		}

		private string SelectFileUsingDialog()
		{
			var fileType = ".mp3";
			var dlg = new DialogAdapters.OpenFileDialogAdapter
			{
				Multiselect = false,
				CheckFileExists = true,
				Filter = $"{fileType} files|*{fileType}"
			};
			var result = dlg.ShowDialog();
			if (result == DialogResult.OK)
			{
				// We are not trying get a memory or time diff, just a point measure.
				PerformanceMeasurement.Global.Measure("Choose file", dlg.FileName)?.Dispose();

				return dlg.FileName.Replace("\\", "/");
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
						var currentBookAudioDirectoryPath = AudioProcessor.GetAudioFolderPath(CurrentBook.FolderPath);
						Directory.CreateDirectory(currentBookAudioDirectoryPath);
						request.ReplyWithText(currentBookAudioDirectoryPath.Replace("\\", "/"));
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
			}
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
				RobustFile.Copy(jsonData.from, jsonData.to, true);
			}
			catch (Exception e)
			{
				request.Failed(HttpStatusCode.InternalServerError, "InternalServerError while copying file. " + e.ToString());
				return;
			}

			request.PostSucceeded();
		}
	}

	enum SpecialLocation
	{
		CurrentBookAudioDirectory
	}
}
