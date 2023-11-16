using System;
using System.Linq;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;

namespace Bloom.web.controllers
{
	/// <summary>
	/// Provide values needed by the Book Info Indicator
	/// </summary>
	public class IndicatorInfoApi
	{
		private readonly CollectionModel _collectionModel;

		public IndicatorInfoApi(CollectionModel collectionModel)
		{
			this._collectionModel = collectionModel;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("book/indicatorInfo", HandleIndicatorInfo,false);
		}

		private void HandleIndicatorInfo(ApiRequest request)
		{
			switch (request.HttpMethod)
			{
				case HttpMethods.Get:
					BookInfo bookInfo = null;
					BookCollection collection;
					if (GetBookInfo(request, out bookInfo, out collection))
					{
						var data = new
						{
							id = bookInfo.Id,
							factoryInstalled = collection.IsFactoryInstalled,
							// Note, sometimes CssThemeWeWillActuallyUse just hasn't been computed yet. For now, we're going to live with that.
							// The UI will just not list it.
							cssThemeWeWillActuallyUse = bookInfo.AppearanceSettings.CssThemeWeWillActuallyUse ?? "",
							firstPossiblyOffendingCssFile = bookInfo.AppearanceSettings.FirstPossiblyOffendingCssFile,
							offendingCss = bookInfo.AppearanceSettings.OffendingCssRule,
							substitutedCssFile = bookInfo.AppearanceSettings.SubstitutedCssFile,
							path = bookInfo.FolderPath,

						};
						request.ReplyWithJson(data);
					}
					else
					{
						request.ReplyWithJson(new
						{
							// user won't see this message, the UI just sees that there is an error and hides the indicator
							error = "Could not find a book with that id."
						});
						return;
					}
					break;

				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		private bool GetBookInfo(ApiRequest request, out BookInfo bookInfoOut, out BookCollection collectionOut)
		{
			var id = request.RequiredParam("id").Trim();
			BookInfo bookInfo = null;
			// get the book info by looking in each of the collections in the _collectionModel for the book with this id
			var collection = _collectionModel.GetBookCollections().FirstOrDefault(c => {
					var bi = c.GetBookInfoById(id);
					if (bi != null)
						bookInfo = bi;
					return bi != null;
			});
			bookInfoOut = bookInfo;
			collectionOut = collection;

			return bookInfo != null;
		}
	}
}
