using System;
using System.Drawing;
using System.Net;
using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;
using SIL.Core.ClearShare;
using SIL.Extensions;
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
            // The next two endpoints support the "reuse metadata from another image" chooser.
            // They are read-only and are driven one image at a time from the front-end so that
            // gathering can be progressive and can stop when the dialog is closed.
            apiHandler.RegisterEndpointHandler(
                "copyrightAndLicense/imageFileNamesInBook",
                HandleImageFileNamesInBook,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "copyrightAndLicense/imageMetadataForFile",
                HandleImageMetadataForFile,
                false
            );
        }

        // Returns the file names of the "real" images in the current book (excluding license,
        // branding, and placeholder images). The metadata chooser fetches each one's metadata
        // separately so it can show results as they arrive.
        private void HandleImageFileNamesInBook(ApiRequest request)
        {
            var domBody = Model.CurrentBook.RawDom.DocumentElement.SelectSingleNode("//body");
            var langs = Model.CurrentBook.GetLanguagePrioritiesForLocalizedTextOnPage();
            var names = ImageApi.GetCreditableImageFileNamesInBook(domBody, langs);
            request.ReplyWithJson(names);
        }

        // Returns the copyright/license metadata for a single image file (by file name) in the
        // same JSON shape that the dialog's image GET uses. Read-only: unlike
        // HandleImageCopyrightAndLicense, this does not prepare the image for editing.
        private void HandleImageMetadataForFile(ApiRequest request)
        {
            var fileName = request.RequiredParam("fileName");
            var path = Model.CurrentBook.FolderPath.CombineForPath(fileName);
            if (!RobustFile.Exists(path))
            {
                request.ReplyWithJson(String.Empty);
                return;
            }
            try
            {
                var metadata = RobustFileIO.MetadataFromFile(path);
                request.ReplyWithJson(GetJsonFromMetadata(metadata, forBook: false));
            }
            catch (Exception e)
            {
                // A corrupt or unreadable image must not break gathering. Log it and reply with
                // empty so the chooser simply skips this image instead of surfacing an error.
                Logger.WriteEvent(
                    $"MetadataChooser: could not read metadata for image '{fileName}': {e.Message}"
                );
                request.ReplyWithJson(String.Empty);
            }
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
                        licenseImage = (
                            CreativeCommonsLicense.FromToken(token[0]) as ILicenseWithImage
                        )?.GetImage();
                    else
                        licenseImage = (
                            request.CurrentBook.GetLicenseMetadata().License as ILicenseWithImage
                        )?.GetImage();
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
                    try
                    {
                        metadata = View.PrepareToEditImageMetadata(imageUrl);
                    }
                    catch (InvalidOperationException e)
                    {
                        request.Failed(HttpStatusCode.BadRequest, e.Message);
                        return;
                    }
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
                    // The dialog's "Copy to all other images in the book" button sets this.
                    // (We no longer pop up a question asking whether to copy to all images.)
                    bool applyToAllImages = request.GetParamOrNull("applyToAllImages") == "true";
                    if (View.MetadataEditIsFromImageToolbox)
                    {
                        // Invoke the palaso image toolbox's save callback directly so that the
                        // toolbox UI immediately shows the new copyright/license information.
                        // However, since we launched this dialog from the image chooser, and
                        // have not yet committed to a new image, we don't want to save the page
                        // and save the metadata to the current image file, if any.
                        View.ApplyImageMetadataToImageToolbox(metadata);
                        request.PostSucceeded();
                        break;
                    }

                    View.Model.SaveThen(
                        () =>
                        { // Saved DOM must be up to date with possibly new imageUrl
                            try
                            {
                                bool wasNormalSuccessfulSave = View.SaveImageMetadata(metadata);
                                // The filename can be null if coming in from the libpalaso toolbox callback,
                                // in which case wasNormalSuccessfulSave will be false anyway.
                                bool isNormalImageType =
                                    View.FileNameOfImageBeingModified != null
                                    && ImageUpdater.IsNormalImagePath(
                                        View.FileNameOfImageBeingModified
                                    );
                                if (
                                    applyToAllImages
                                    && wasNormalSuccessfulSave
                                    && isNormalImageType
                                )
                                {
                                    // Copies the metadata to every image (runs synchronously).
                                    View.CopyImageMetadataToAllImages(metadata);
                                }
                                else if (wasNormalSuccessfulSave)
                                {
                                    View.UpdateMetadataForCurrentImage(); // Need to get things up to date on the current page.
                                }
                            }
                            finally
                            {
                                // Always tell the (still-open) dialog that the "Add this info to
                                // all images" request has finished, so it can replace its
                                // "Working…" spinner with a "done" confirmation. We must do this
                                // even when the copy didn't actually run (save failed, non-normal
                                // image, or an error above) so the spinner never hangs forever.
                                if (applyToAllImages)
                                    View.Model.NotifyCopyrightPushedToAllImages();
                            }
                            return View.Model.CurrentPage.Id;
                        },
                        () =>
                        {
                            // We weren't in a state to save, so nothing happened; still clear the
                            // dialog's "Working…" spinner if it is waiting on an "Add this info to
                            // all images" request.
                            if (applyToAllImages)
                                View.Model.NotifyCopyrightPushedToAllImages();
                        }
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
                    originalLicense = GetLicense(originalMetadata),
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
            if (IsCreativeCommonsLicense(metadata.License))
                creativeCommonsInfoJson = GetCreativeCommonsInfo(metadata.License);
            return new
            {
                licenseType = GetLicenseType(metadata.License),
                creativeCommonsInfo = creativeCommonsInfoJson,
                rightsStatement = metadata.License?.RightsStatement ?? string.Empty,
            };
        }

        private dynamic GetDefaultCreativeCommonsInfo()
        {
            return new
            {
                allowCommercial = "yes",
                allowDerivatives = "yes",
                intergovernmentalVersion = false,
            };
        }

        private dynamic GetCreativeCommonsInfo(SIL.Core.ClearShare.LicenseInfo ccLicense)
        {
            dynamic dynamicCcLicense = ccLicense;

            return new
            {
                token = dynamicCcLicense.Token,
                allowCommercial = dynamicCcLicense.CommercialUseAllowed ? "yes" : "no",
                allowDerivatives = GetCcDerivativeRulesAsString(dynamicCcLicense.DerivativeRule),
                intergovernmentalVersion = dynamicCcLicense.IntergovernmentalOrganizationQualifier,
            };
        }

        private static string GetCcDerivativeRulesAsString(object rules)
        {
            switch (rules?.ToString())
            {
                case "DerivativesWithShareAndShareAlike":
                    return "sharealike";
                case "Derivatives":
                    return "yes";
                case "NoDerivatives":
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

        private string GetLicenseType(SIL.Core.ClearShare.LicenseInfo licenseInfo)
        {
            if (IsCreativeCommonsLicense(licenseInfo))
            {
                if (licenseInfo.Url == CreativeCommonsLicense.CC0Url)
                    return "publicDomain";
                return "creativeCommons";
            }
            if (IsCustomLicense(licenseInfo))
                return "custom";
            return "contact";
        }

        private static bool IsCreativeCommonsLicense(SIL.Core.ClearShare.LicenseInfo licenseInfo)
        {
            return licenseInfo is CreativeCommonsLicense
                || licenseInfo?.GetType().Name == "CreativeCommonsLicenseInfo";
        }

        private static bool IsCustomLicense(SIL.Core.ClearShare.LicenseInfo licenseInfo)
        {
            return licenseInfo is CustomLicense
                || licenseInfo?.GetType().Name == "CustomLicenseInfo";
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
                        .intergovernmentalVersion,
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
