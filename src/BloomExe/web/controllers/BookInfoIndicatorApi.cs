using System;
using System.Linq;
using Bloom.Api;
using Bloom.Book;

namespace Bloom.web.controllers
{
	/// <summary>
	/// Provide values needed by the Book Info Indicator
	/// </summary>
	public class OtherBookInfoApi
	{
		private readonly CurrentEditableCollectionSelection _currentBookCollectionSelection;

		public OtherBookInfoApi(CurrentEditableCollectionSelection currentBookCollectionSelection)
		{
			_currentBookCollectionSelection = currentBookCollectionSelection;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("book/otherInfo", HandleBookInfo,false);
		}

		private void HandleBookInfo(ApiRequest request)
		{
			switch (request.HttpMethod)
			{
				case HttpMethods.Get:
					var bookInfo = GetBookInfo(request);

					var data = new
					{
						id = bookInfo.Id,
						cssThemeWeWillActuallyUse = bookInfo.AppearanceSettings.CssThemeWeWillActuallyUse ?? "Not ready yet",
						firstPossiblyLegacyCss = bookInfo.AppearanceSettings.FirstPossiblyLegacyCss,
						path = bookInfo.FolderPath,

					};
					request.ReplyWithJson(data);
					break;

				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		private BookInfo GetBookInfo(ApiRequest request)
		{
			var id = request.RequiredParam("id").Trim();
			return _currentBookCollectionSelection.CurrentSelection.GetBookInfos().FirstOrDefault(b => b.Id == id);
		}
	}
}
