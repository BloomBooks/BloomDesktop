using System;
using Bloom.Api;
using Bloom.Book;
using L10NSharp;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Exposes values needed by the Book Metadata Dialog via API
    /// </summary>
    public class BookMetadataApi
    {
        private readonly BookSelection _bookSelection;

        public BookMetadataApi(BookSelection bookSelection)
        {
            _bookSelection = bookSelection;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            bool requiresSync = false; // Lets us open the dialog while the epub preview is being generated
            apiHandler.RegisterEndpointHandler(
                "book/metadata",
                HandleBookMetadata,
                false,
                requiresSync
            );
        }

        private void HandleBookMetadata(ApiRequest request)
        {
            switch (request.HttpMethod)
            {
                case HttpMethods.Get:
                    // The spec is here: https://docs.google.com/document/d/e/2PACX-1vREQ7fUXgSE7lGMl9OJkneddkWffO4sDnMG5Vn-IleK35fJSFqnC-6ulK1Ss3eoETCHeLn0wPvcxJOf/pub
                    // See also https://www.w3.org/Submission/2017/SUBM-epub-a11y-20170125/#sec-conf-reporting.
                    var licenseUrl = _bookSelection.CurrentSelection
                        .GetLicenseMetadata()
                        .License.Url;
                    if (string.IsNullOrEmpty(licenseUrl))
                        licenseUrl = null; // allows us to use ?? below.
                    var metadata = new
                    {
                        metapicture = new
                        {
                            type = "image",
                            value = "/bloom/" + _bookSelection.CurrentSelection.GetCoverImagePath(),
                            translatedLabel = LocalizationManager.GetString(
                                "BookMetadata.metapicture",
                                "Picture"
                            )
                        },
                        name = new
                        {
                            type = "readOnlyText",
                            value = _bookSelection.CurrentSelection.NameBestForUserDisplay,
                            translatedLabel = LocalizationManager.GetString(
                                "BookMetadata.name",
                                "Name"
                            )
                        },
                        numberOfPages = new
                        {
                            type = "readOnlyText",
                            value = _bookSelection.CurrentSelection
                                .GetLastNumberedPageNumber()
                                .ToString(),
                            translatedLabel = LocalizationManager.GetString(
                                "BookMetadata.numberOfPages",
                                "Number of pages"
                            )
                        },
                        inLanguage = new
                        {
                            type = "readOnlyText",
                            value = _bookSelection.CurrentSelection.BookData.Language1.Tag,
                            translatedLabel = LocalizationManager.GetString(
                                "BookMetadata.inLanguage",
                                "Language"
                            )
                        },
                        // "All rights reserved" is purposely not localized, so it remains an accurate representation of
                        // the English information that will be put in the file in place of a License URL.
                        License = new
                        {
                            type = "readOnlyText",
                            value = licenseUrl ?? @"All rights reserved",
                            translatedLabel = LocalizationManager.GetString(
                                "Common.License",
                                "License"
                            )
                        },
                        author = new
                        {
                            type = "editableText",
                            value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.Author,
                            translatedLabel = LocalizationManager.GetString(
                                "BookMetadata.author",
                                "Author"
                            )
                        },
                        summary = new
                        {
                            type = "bigEditableText",
                            value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.Summary,
                            translatedLabel = LocalizationManager.GetString(
                                "PublishTab.Upload.Summary",
                                "Summary"
                            )
                        },
                        typicalAgeRange = new
                        {
                            type = "editableText",
                            value = ""
                                + _bookSelection.CurrentSelection.BookInfo.MetaData.TypicalAgeRange,
                            translatedLabel = LocalizationManager.GetString(
                                "BookMetadata.typicalAgeRange",
                                "Typical age range"
                            )
                        },
                        level = new
                        {
                            type = "editableText",
                            value = ""
                                + _bookSelection
                                    .CurrentSelection
                                    .BookInfo
                                    .MetaData
                                    .ReadingLevelDescription,
                            translatedLabel = LocalizationManager.GetString(
                                "BookMetadata.level",
                                "Reading level"
                            )
                        },
                        subjects = new
                        {
                            type = "subjects",
                            value = _bookSelection.CurrentSelection.BookInfo.MetaData.Subjects,
                            translatedLabel = LocalizationManager.GetString(
                                "BookMetadata.subjects",
                                "Subjects"
                            )
                        },
                        a11yLevel = new
                        {
                            type = "a11yLevel",
                            value = ""
                                + _bookSelection.CurrentSelection.BookInfo.MetaData.A11yLevel,
                            translatedLabel = LocalizationManager.GetString(
                                "BookMetadata.a11yLevel",
                                "Accessibility level"
                            ),
                            helpurl = "http://www.idpf.org/epub/a11y/accessibility.html#sec-acc-pub-wcag"
                        },
                        a11yCertifier = new
                        {
                            type = "editableText",
                            value = ""
                                + _bookSelection.CurrentSelection.BookInfo.MetaData.A11yCertifier,
                            translatedLabel = LocalizationManager.GetString(
                                "BookMetadata.a11yCertifier",
                                "Level certified by"
                            )
                        },
                        hazards = new
                        {
                            type = "hazards",
                            value = "" + _bookSelection.CurrentSelection.BookInfo.MetaData.Hazards,
                            translatedLabel = LocalizationManager.GetString(
                                "BookMetadata.hazards",
                                "Hazards"
                            ),
                            helpurl = "http://www.idpf.org/epub/a11y/techniques/techniques.html#meta-004"
                        },
                        a11yFeatures = new
                        {
                            type = "a11yFeatures",
                            value = ""
                                + _bookSelection.CurrentSelection.BookInfo.MetaData.A11yFeatures,
                            translatedLabel = LocalizationManager.GetString(
                                "BookMetadata.a11yFeatures",
                                "Accessibility features"
                            ),
                            helpurl = "http://www.idpf.org/epub/a11y/techniques/techniques.html#meta-003"
                        }
                    };
                    var translatedStringPairs = new
                    {
                        flashingHazard = LocalizationManager.GetString(
                            "BookMetadata.flashingHazard",
                            "Flashing Hazard"
                        ),
                        motionSimulationHazard = LocalizationManager.GetString(
                            "BookMetadata.motionSimulationHazard",
                            "Motion Simulation Hazard"
                        ),
                        alternativeText = LocalizationManager.GetString(
                            "BookMetadata.alternativeText",
                            "Has Image Descriptions"
                        ),
                        signLanguage = LocalizationManager.GetString(
                            "BookMetadata.signLanguage",
                            "Sign Language"
                        ),
                    };
                    var blob = new { metadata, translatedStringPairs, };
                    request.ReplyWithJson(blob);
                    break;
                case HttpMethods.Post:
                    var json = request.RequiredPostJson();
                    var settings = DynamicJson.Parse(json);
                    _bookSelection.CurrentSelection.BookInfo.MetaData.Author = settings[
                        "author"
                    ].value.Trim();
                    _bookSelection.CurrentSelection.BookInfo.MetaData.Summary = settings[
                        "summary"
                    ].value.Trim();
                    _bookSelection.CurrentSelection.BookInfo.MetaData.TypicalAgeRange = settings[
                        "typicalAgeRange"
                    ].value.Trim();
                    _bookSelection.CurrentSelection.BookInfo.MetaData.ReadingLevelDescription =
                        settings["level"].value.Trim();
                    _bookSelection.CurrentSelection.BookInfo.MetaData.Subjects = settings[
                        "subjects"
                    ].value;
                    _bookSelection.CurrentSelection.BookInfo.MetaData.A11yLevel = settings[
                        "a11yLevel"
                    ].value.Trim();
                    _bookSelection.CurrentSelection.BookInfo.MetaData.A11yCertifier = settings[
                        "a11yCertifier"
                    ].value.Trim();
                    _bookSelection.CurrentSelection.BookInfo.MetaData.Hazards = settings[
                        "hazards"
                    ].value.Trim();
                    _bookSelection.CurrentSelection.BookInfo.MetaData.A11yFeatures = settings[
                        "a11yFeatures"
                    ].value.Trim();
                    _bookSelection.CurrentSelection.Save();
                    request.PostSucceeded();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
