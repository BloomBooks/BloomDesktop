using Bloom.Api;
using Bloom.Book;
using Bloom.Publish;
using Bloom.Utils;
using Newtonsoft.Json;
using SIL.IO;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Forms;

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
	}
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
			apiHandler.RegisterEndpointLegacy("fileIO/chooseFile", ChooseFile, true);
			apiHandler.RegisterEndpointLegacy("fileIO/getSpecialLocation", GetSpecialLocation, true);
			apiHandler.RegisterEndpointLegacy("fileIO/copyFile", CopyFile, true);
		}

		private void ChooseFile(ApiRequest request)
		{
			lock (request)
			{
				string json = request.RequiredPostJson();
				OpenFileRequest requestParameters = JsonConvert.DeserializeObject<OpenFileRequest>(json);

				request.ReplyWithText(SelectFileUsingDialog(requestParameters));
			}
		}

		private string SelectFileUsingDialog(OpenFileRequest requestParameters)
		{
			var dlg = new DialogAdapters.OpenFileDialogAdapter
			{
				Title = requestParameters.title,
				Multiselect = false,
				CheckFileExists = true,
				Filter = string.Join("|", requestParameters.fileTypes.Select(fileType => $"{fileType.name}|{string.Join(";", fileType.extensions.Select(e => "*." + e))}"))
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
