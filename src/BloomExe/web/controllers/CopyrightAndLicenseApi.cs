using System;
using System.Drawing;
using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;
using SIL.IO;
using SIL.Reporting;
using SIL.Windows.Forms.ClearShare;

namespace Bloom.web.controllers
{
    // Handles api calls for the Copyright and License dialog
    public class CopyrightAndLicenseApi
    {
        public EditingModel Model { get; set; }
        public EditingView View { get; set; }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(
                "copyrightAndLicense/ccImage",
                HandleGetCCImage,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "copyrightAndLicense/bookCopyrightAndLicense",
                HandleBookCopyrightAndLicense,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "copyrightAndLicense/imageCopyrightAndLicense",
                HandleImageCopyrightAndLicense,
                true
            );
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
                    // ReplyWithImage uses the extension to determine the content type
                    using (TempFile tempFile = TempFile.WithExtension(".png"))
                    {
                        licenseImage.Save(tempFile.Path);
                        request.ReplyWithImage(tempFile.Path);
                    }
                    licenseImage.Dispose();
                }
                catch (Exception e)
                {
                    Logger.WriteError("Unable to get cc license image for dialog", e);
                    request.Failed();
                }
            }
        }

        private void HandleBookCopyrightAndLicense(ApiRequest request)
        {
            switch (request.HttpMethod)
            {
                case HttpMethods.Get:

                    var intellectualPropertyData = GetJsonFromMetadata(
                        Model.CurrentBook.GetLicenseMetadata(),
                        forBook: true
                    );
                    request.ReplyWithJson(intellectualPropertyData);
                    break;
                case HttpMethods.Post:
                    try
                    {
                        Model.ChangeBookLicenseMetaData(
                            GetMetadataFromJson(request, forBook: true)
                        );
                    }
                    catch (Exception ex)
                    {
                        ErrorReport.NotifyUserOfProblem(
                            ex,
                            "There was a problem recording your changes to the copyright and license."
                        );
                    }
                    request.PostSucceeded();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void HandleImageCopyrightAndLicense(ApiRequest request)
        {
            Metadata metadata;
            switch (request.HttpMethod)
            {
                case HttpMethods.Get:
                    var imageUrl = request.Parameters["imageUrl"]; // might be null
                    metadata = View.PrepareToEditImageMetadata(imageUrl);
                    if (metadata == null)
                    {
                        request.ReplyWithJson(String.Empty);
                        return;
                    }
                    Logger.WriteEvent("Showing Copyright/License Editor For Image");
                    var intellectualPropertyData = GetJsonFromMetadata(metadata, forBook: false);
                    request.ReplyWithJson(intellectualPropertyData);
                    break;
                case HttpMethods.Post:
                    metadata = GetMetadataFromJson(request, forBook: false);
                    View.Model.SaveThen(
                        () =>
                        { // Saved DOM must be up to date with possibly new imageUrl
                            bool wasNormalSuccessfulSave = View.SaveImageMetadata(metadata);
                            bool isNormalImageType = ImageUpdater.IsNormalImagePath(
                                View.FileNameOfImageBeingModified
                            );
                            bool shouldAskUserIfCopyMetadataToAllImages =
                                wasNormalSuccessfulSave && isNormalImageType;
                            bool copyMetadataToAllImages = shouldAskUserIfCopyMetadataToAllImages
                                ? View.AskUserIfCopyImageMetadataToAllImages(metadata)
                                : false;
                            if (copyMetadataToAllImages)
                            {
                                View.CopyImageMetadataToAllImages(metadata);
                            }
                            else if (wasNormalSuccessfulSave)
                            {
                                View.UpdateMetadataForCurrentImage(); // Need to get things up to date on the current page.
                            }
                            return View.Model.CurrentPage.Id;
                        },
                        () => { } // wrong state, do nothing
                    );

                    request.PostSucceeded();

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public object GetJsonFromMetadata(Metadata metadata, bool forBook)
        {
            dynamic derivativeInfo = null;
            if (forBook)
            {
                var originalMetadata = BookCopyrightAndLicense.GetOriginalMetadata(
                    Model.CurrentBook.Storage.Dom,
                    Model.CurrentBook.BookData
                );
                var languagePriorityIdsNotLang1 =
                    Model.CurrentBook.BookData.GetLanguagePrioritiesForLocalizedTextOnPage(false);
                var licenseText = BookCopyrightAndLicense.GetOriginalLicenseSentence(
                    languagePriorityIdsNotLang1,
                    originalMetadata.License,
                    out string _
                );
                derivativeInfo = new
                {
                    isBookDerivative = BookCopyrightAndLicense.IsDerivative(originalMetadata),
                    useOriginalCopyright = Model.CurrentBook.BookInfo.MetaData.UseOriginalCopyright,
                    originalCopyrightAndLicenseText = $"{originalMetadata.CopyrightNotice}, {licenseText}",
                    originalCopyrightYear = originalMetadata.GetCopyrightYear(),
                    originalCopyrightHolder = originalMetadata.GetCopyrightBy(),
                    originalLicense = GetLicense(originalMetadata)
                };
            }

            var intellectualPropertyData = new
            {
                derivativeInfo = derivativeInfo,
                copyrightInfo = new
                {
                    imageCreator = metadata.Creator ?? string.Empty,
                    copyrightYear = metadata.GetCopyrightYear() ?? string.Empty,
                    copyrightHolder = metadata.GetCopyrightBy() ?? string.Empty,
                },
                licenseInfo = GetLicense(metadata),
            };
            return intellectualPropertyData;
        }

        private dynamic GetLicense(Metadata metadata)
        {
            dynamic creativeCommonsInfoJson = GetDefaultCreativeCommonsInfo();
            if (metadata.License is CreativeCommonsLicense)
                creativeCommonsInfoJson = GetCreativeCommonsInfo(
                    (CreativeCommonsLicense)metadata.License
                );
            return new
            {
                licenseType = GetLicenseType(metadata.License),
                creativeCommonsInfo = creativeCommonsInfoJson,
                rightsStatement = metadata.License?.RightsStatement ?? string.Empty
            };
        }

        private dynamic GetDefaultCreativeCommonsInfo()
        {
            return new
            {
                allowCommercial = "yes",
                allowDerivatives = "yes",
                intergovernmentalVersion = false
            };
        }

        private dynamic GetCreativeCommonsInfo(CreativeCommonsLicense ccLicense)
        {
            return new
            {
                token = ccLicense.Token,
                allowCommercial = ccLicense.CommercialUseAllowed ? "yes" : "no",
                allowDerivatives = GetCcDerivativeRulesAsString(ccLicense.DerivativeRule),
                intergovernmentalVersion = ccLicense.IntergovernmentalOrganizationQualifier
            };
        }

        private static string GetCcDerivativeRulesAsString(
            CreativeCommonsLicense.DerivativeRules rules
        )
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

        private CreativeCommonsLicense.DerivativeRules GetCcDerivativeRule(string jsonValue)
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
            {
                if (licenseInfo.Url == CreativeCommonsLicense.CC0Url)
                    return "publicDomain";
                return "creativeCommons";
            }
            if (licenseInfo is CustomLicense)
                return "custom";
            return "contact";
        }

        private Metadata GetMetadataFromJson(ApiRequest request, bool forBook)
        {
            var json = request.RequiredPostJson();
            var settings = DynamicJson.Parse(json);

            if (forBook)
            {
                Model.CurrentBook.BookInfo.MetaData.UseOriginalCopyright = settings
                    .derivativeInfo
                    .useOriginalCopyright;

                if (settings.derivativeInfo.useOriginalCopyright)
                    return BookCopyrightAndLicense.GetOriginalMetadata(
                        Model.CurrentBook.Storage.Dom,
                        Model.CurrentBook.BookData
                    );
            }

            var metadata = new Metadata { Creator = settings.copyrightInfo.imageCreator };
            metadata.SetCopyrightNotice(
                settings.copyrightInfo.copyrightYear,
                settings.copyrightInfo.copyrightHolder
            );

            if (settings.licenseInfo.licenseType == "creativeCommons")
            {
                metadata.License = new CreativeCommonsLicense(
                    true,
                    settings.licenseInfo.creativeCommonsInfo.allowCommercial == "yes",
                    GetCcDerivativeRule(settings.licenseInfo.creativeCommonsInfo.allowDerivatives)
                )
                {
                    IntergovernmentalOrganizationQualifier = settings
                        .licenseInfo
                        .creativeCommonsInfo
                        .intergovernmentalVersion
                };
            }
            else if (settings.licenseInfo.licenseType == "publicDomain")
            {
                metadata.License = CreativeCommonsLicense.FromToken("cc0");
            }
            else if (settings.licenseInfo.licenseType == "contact")
            {
                metadata.License = new NullLicense();
            }
            else
            {
                metadata.License = new CustomLicense();
            }

            metadata.License.RightsStatement = settings.licenseInfo.rightsStatement;

            return metadata;
        }
    }
}
