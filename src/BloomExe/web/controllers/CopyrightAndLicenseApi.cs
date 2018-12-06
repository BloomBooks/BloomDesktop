using System;
using System.Drawing;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Edit;
using L10NSharp;
using SIL.IO;
using SIL.Reporting;
using SIL.Windows.Forms.ClearShare;

namespace Bloom.web.controllers
{
	public class CopyrightAndLicenseApi
	{
		public EditingModel Model { get; set; }
		public EditingView View { get; set; }

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("copyrightAndLicense/ccImage", HandleGetCCImage, false);
			apiHandler.RegisterEndpointHandler("copyrightAndLicense/bookCopyrightAndLicense", HandleBookCopyrightAndLicense, true);
			apiHandler.RegisterEndpointHandler("copyrightAndLicense/imageCopyrightAndLicense", HandleImageCopyrightAndLicense, true);
			apiHandler.RegisterEndpointHandler("copyrightAndLicense/cancel", HandleCancel, true);
		}

		private void HandleGetCCImage(ApiRequest request)
		{
			lock (request)
			{
				try
				{
					Image licenseImage;
					var token = request.Parameters.GetValues("token");
					if (token != null)
						licenseImage = CreativeCommonsLicense.FromToken(token[0]).GetImage();
					else
						licenseImage = request.CurrentBook.GetLicenseMetadata().License.GetImage();
					using (TempFile tempFile = new TempFile())
					{
						licenseImage.Save(tempFile.Path);
						request.ReplyWithImage(tempFile.Path);
					}
				}
				catch
				{
					request.Failed();
				}
			}
		}

		private void HandleBookCopyrightAndLicense(ApiRequest request)
		{
			switch (request.HttpMethod)
			{
				case HttpMethods.Get:
					//in case we were in this dialog already and made changes which haven't found their way out to the book yet
					Model.SaveNow();

					var intellectualPropertyData = GetJsonFromMetadata(Model.CurrentBook.GetLicenseMetadata());
					request.ReplyWithJson(intellectualPropertyData);
					break;
				case HttpMethods.Post:
					try
					{
						Model.ChangeBookLicenseMetaData(GetMetadataFromJson(request));
					}
					catch (Exception ex)
					{
						ErrorReport.NotifyUserOfProblem(ex, "There was a problem recording your changes to the copyright and license.");
					}
					request.PostSucceeded();
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		private void HandleImageCopyrightAndLicense(ApiRequest request)
		{
			switch (request.HttpMethod)
			{
				case HttpMethods.Get:
					var intellectualPropertyData = GetJsonFromMetadata(View.ImageBeingModified.Metadata);
					request.ReplyWithJson(intellectualPropertyData);
					break;
				case HttpMethods.Post:
					var metadata = GetMetadataFromJson(request);

					View.ImageBeingModified.Metadata = metadata;
					View.ImageBeingModified.Metadata.StoreAsExemplar(Metadata.FileCategory.Image);
					//update so any overlays on the image are brought up to data
					PageEditingModel.UpdateMetadataAttributesOnImage(new ElementProxy(View.ImageElementBeingModified), View.ImageBeingModified);
					View.ImageElementBeingModified.Click(); //wake up javascript to update overlays
					View.SaveChangedImage(View.ImageElementBeingModified, View.ImageBeingModified, "Bloom had a problem updating the image metadata");

					CleanUpImage();
					request.PostSucceeded();

					View.AskUserToCopyImageMetadataToAllImages(metadata);
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		private void HandleCancel(ApiRequest request)
		{
			CleanUpImage();
			request.PostSucceeded();
		}

		private void CleanUpImage()
		{
			View.ImageBeingModified?.Dispose();
			View.ImageBeingModified = null;
			View.ImageElementBeingModified = null;
		}

		private object GetJsonFromMetadata(Metadata metadata)
		{
			dynamic creativeCommonsInfoJson = GetDefaultCreativeCommonsInfo();
			if (metadata.License is CreativeCommonsLicense)
				creativeCommonsInfoJson = GetCreativeCommonsInfo((CreativeCommonsLicense)metadata.License);

			var intellectualPropertyData = new
			{
				creator = metadata.Creator ?? string.Empty,
				copyrightYear = metadata.GetCopyrightYear() ?? string.Empty,
				copyrightHolder = metadata.GetCopyrightBy() ?? string.Empty,
				licenseInfo = new
				{
					licenseType = GetLicenseType(metadata.License),
					creativeCommonsInfo = creativeCommonsInfoJson,
					rightsStatement = metadata.License.RightsStatement ?? string.Empty
				},
			};
			return intellectualPropertyData;
		}

		private dynamic GetDefaultCreativeCommonsInfo()
		{
			return new
			{
				allowCommercial = "yes",
				allowDerivatives = "yes",
				intergovernmentalVersion = true
			};
		}

		private dynamic GetCreativeCommonsInfo(CreativeCommonsLicense ccLicense)
		{
			return new
			{
				allowCommercial = ccLicense.CommercialUseAllowed ? "yes" : "no",
				allowDerivatives = GetDerivativeRulesAsString(ccLicense.DerivativeRule),
				intergovernmentalVersion = ccLicense.IntergovernmentalOriganizationQualifier
			};
		}

		private static string GetDerivativeRulesAsString(CreativeCommonsLicense.DerivativeRules rules)
		{
			switch (rules)
			{
				case CreativeCommonsLicense.DerivativeRules.DerivativesWithShareAndShareAlike:
					return "sharealike";
				case CreativeCommonsLicense.DerivativeRules.Derivatives:
					return "yes";
				case CreativeCommonsLicense.DerivativeRules.NoDerivatives:
					return "no";
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		private CreativeCommonsLicense.DerivativeRules GetDerivativeRule(string jsonValue)
		{
			switch (jsonValue)
			{
				case "sharealike":
					return CreativeCommonsLicense.DerivativeRules.DerivativesWithShareAndShareAlike;
				case "yes":
					return CreativeCommonsLicense.DerivativeRules.Derivatives;
				case "no":
					return CreativeCommonsLicense.DerivativeRules.NoDerivatives;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		private string GetLicenseType(LicenseInfo licenseInfo)
		{
			if (licenseInfo is CreativeCommonsLicense)
				return "creativeCommons";
			if (licenseInfo is CustomLicense)
				return "custom";
			return "contact";
		}

		private Metadata GetMetadataFromJson(ApiRequest request)
		{
			var json = request.RequiredPostJson();
			var settings = DynamicJson.Parse(json);

			var metadata = new Metadata {Creator = settings.creator};
			metadata.SetCopyrightNotice(settings.copyrightYear, settings.copyrightHolder);

			if (settings.licenseInfo.licenseType == "creativeCommons")
			{
				metadata.License = new CreativeCommonsLicense(
					true,
					settings.licenseInfo.creativeCommonsInfo.allowCommercial == "yes",
					GetDerivativeRule(settings.licenseInfo.creativeCommonsInfo.allowDerivatives))
				{
					IntergovernmentalOriganizationQualifier = settings.licenseInfo.creativeCommonsInfo.intergovernmentalVersion
				};
			}
			else if (settings.licenseInfo.licenseType == "contact")
				metadata.License = new NullLicense();
			else
				metadata.License = new CustomLicense();

			metadata.License.RightsStatement = settings.licenseInfo.rightsStatement;

			return metadata;
		}
	}
}
