using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Collection;
using Bloom.Properties;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using SIL.Extensions;

namespace Bloom.web.controllers
{
    /// <summary>
    /// The reply to GET collection/settings. The TypeScript interface ICollectionSettingsResponse
    /// in src/BloomBrowserUI/collection/collectionSettingsTypes.ts must match these classes; they
    /// are serialized with camelCase names.
    /// </summary>
    public class CollectionSettingsResponse
    {
        public CollectionSettingsValues Values;

        /// <summary>
        /// Dotted paths into Values whose change means Bloom has to restart, so the dialog can
        /// relabel its OK button and show the restart reminder.
        /// </summary>
        public string[] RestartPaths;

        /// <summary>
        /// Whether the open collection is a Team Collection (even if disconnected).
        /// </summary>
        public bool IsTeamCollection;

        /// <summary>
        /// Set only when the user may not edit the collection settings (a Team Collection member
        /// who is not an administrator); the other fields are then null.
        /// </summary>
        public string NotAllowedMessage;
    }

    /// <summary>
    /// Marks a value whose change means Bloom has to restart. GetRestartPaths collects them.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class RequiresRestartAttribute : Attribute { }

    /// <summary>
    /// The editable settings, grouped by the page of the dialog that shows them. Most of them
    /// are a live view of a CollectionSettings: serializing reads the collection's values, and
    /// populating from the posted JSON writes them straight back, so a simple setting is spelled
    /// out once here, as a property that reads and writes it. The languages are the exception;
    /// see Languages. The subscription and the team collection administrators have endpoints of
    /// their own and so are not here.
    /// </summary>
    public class CollectionSettingsValues
    {
        private readonly CollectionSettings _settings;

        /// <param name="queueRenameOfCollection">called with the new folder name when a posted
        /// collection name differs from the current one; only needed when populating</param>
        public CollectionSettingsValues(
            CollectionSettings settings,
            Action<string> queueRenameOfCollection = null
        )
        {
            _settings = settings;
            FrontBackMatter = new FrontBackMatterValues(settings);
            BloomLibrary = new BloomLibraryValues(settings);
            Advanced = new AdvancedValues(settings, queueRenameOfCollection);
            Experimental = new ExperimentalValues();
        }

        /// <summary>
        /// The languages have to be applied together (changing one can reorder the collection's
        /// list of languages), so this one is a snapshot rather than a view: populating replaces it
        /// as a whole, and the setter applies it.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public LanguagesValues Languages
        {
            get => LanguagesValues.From(_settings);
            set => CollectionSettingsUpdater.ApplyLanguages(value, _settings);
        }

        public FrontBackMatterValues FrontBackMatter { get; }
        public BloomLibraryValues BloomLibrary { get; }
        public AdvancedValues Advanced { get; }
        public ExperimentalValues Experimental { get; }

        /// <summary>
        /// The dotted paths, as the dialog sees them, of every value marked RequiresRestart.
        /// </summary>
        public static string[] GetRestartPaths()
        {
            var paths = new List<string>();
            AddRestartPaths(typeof(CollectionSettingsValues), "", paths);
            return paths.ToArray();
        }

        private static void AddRestartPaths(Type type, string prefix, List<string> paths)
        {
            var contract = (JsonObjectContract)
                CollectionSettingsApi.kCamelCaseSettings.ContractResolver.ResolveContract(type);
            foreach (var property in contract.Properties.Where(p => !p.Ignored))
            {
                var path = prefix + property.PropertyName;
                if (
                    property
                        .AttributeProvider.GetAttributes(typeof(RequiresRestartAttribute), true)
                        .Any()
                )
                    paths.Add(path);
                else if (
                    property.PropertyType.Namespace == typeof(CollectionSettingsValues).Namespace
                )
                    AddRestartPaths(property.PropertyType, path + ".", paths);
            }
        }
    }

    /// <summary>
    /// A snapshot of the collection's languages; CollectionSettingsUpdater.ApplyLanguages
    /// applies a posted one.
    /// </summary>
    public class LanguagesValues
    {
        public LanguageValues Language1;
        public LanguageValues Language2;

        /// <summary>
        /// Null when the collection has no third language; posting null removes it.
        /// </summary>
        public LanguageValues Language3;

        /// <summary>
        /// Null when the collection has no sign language; posting null removes it.
        /// </summary>
        public SignLanguageValues SignLanguage;

        /// <summary>
        /// The languages as the collection has them now.
        /// </summary>
        public static LanguagesValues From(CollectionSettings settings)
        {
            var thirdLanguage = settings.AllLanguages[2];
            return new LanguagesValues
            {
                Language1 = LanguageValues.From(settings.AllLanguages[0]),
                Language2 = LanguageValues.From(settings.AllLanguages[1]),
                Language3 = string.IsNullOrEmpty(thirdLanguage?.Tag)
                    ? null
                    : LanguageValues.From(thirdLanguage),
                // The collection stores "no sign language" as an empty (or, if never saved,
                // null) tag; the dialog gets null instead.
                SignLanguage = string.IsNullOrEmpty(settings.SignLanguage.Tag)
                    ? null
                    : new SignLanguageValues
                    {
                        Tag = settings.SignLanguage.Tag,
                        Name = settings.SignLanguage.Name,
                        IsCustomName = settings.SignLanguage.IsCustomName,
                    },
            };
        }
    }

    public class LanguageValues
    {
        [RequiresRestart]
        public string Tag;

        [RequiresRestart]
        public string Name;
        public bool IsCustomName;

        [RequiresRestart]
        public string FontName;

        [RequiresRestart]
        public bool IsRightToLeft;
        public decimal LineHeight;
        public bool BreaksLinesOnlyAtSpaces;
        public int BaseUIFontSizeInPoints;

        /// <summary>
        /// One of the collection's languages as it is now.
        /// </summary>
        public static LanguageValues From(WritingSystem language)
        {
            return new LanguageValues
            {
                Tag = language.Tag,
                Name = language.Name,
                IsCustomName = language.IsCustomName,
                FontName = language.FontName,
                IsRightToLeft = language.IsRightToLeft,
                LineHeight = language.LineHeight,
                BreaksLinesOnlyAtSpaces = language.BreaksLinesOnlyAtSpaces,
                BaseUIFontSizeInPoints = language.BaseUIFontSizeInPoints,
            };
        }
    }

    /// <summary>
    /// A sign language has no font, text direction or line spacing.
    /// </summary>
    public class SignLanguageValues
    {
        [RequiresRestart]
        public string Tag;

        [RequiresRestart]
        public string Name;
        public bool IsCustomName;
    }

    /// <summary>
    /// The Front &amp; Back Matter page's values, read from and written to the collection.
    /// </summary>
    public class FrontBackMatterValues
    {
        private readonly CollectionSettings _settings;

        // The caption as the dialog was given it. The default caption is localized using the
        // collection's languages, which may already have changed by the time QrcodeCaption is set.
        private readonly string _qrcodeCaptionBeforeChanges;

        public FrontBackMatterValues(CollectionSettings settings)
        {
            _settings = settings;
            _qrcodeCaptionBeforeChanges = settings.BadgeQrCodeLabelLocalized;
        }

        /// <summary>
        /// CollectionSettingsUpdater.Apply replaces a pack that is not available once all the
        /// values are in.
        /// </summary>
        [RequiresRestart]
        public string Xmatter
        {
            get => _settings.XMatterPackName;
            set => _settings.XMatterPackName = value;
        }

        /// <summary>
        /// A non-localized key.
        /// </summary>
        [RequiresRestart]
        public string PageNumberStyle
        {
            get => _settings.PageNumberStyle;
            set => _settings.PageNumberStyle = value;
        }

        // The QR code settings do not really need anything as drastic as a restart, but the
        // badge in every book has to be rebuilt somehow.
        [RequiresRestart]
        public bool ShowQrCode
        {
            get => _settings.ShowBlorgLanguageQrCode;
            set => _settings.ShowBlorgLanguageQrCode = value;
        }

        [RequiresRestart]
        public string QrcodeCaption
        {
            get => _settings.BadgeQrCodeLabelLocalized;
            set
            {
                // The caption shown by default follows the current UI language, so store it only
                // if the user actually changed it; otherwise it would be frozen in that language.
                if (value != _qrcodeCaptionBeforeChanges)
                    _settings.BadgeQrCodeLabel = value;
            }
        }

        // A collection whose settings file never carried these leaves them null.
        public string Country
        {
            get => _settings.Country ?? "";
            set => _settings.Country = value;
        }
        public string Province
        {
            get => _settings.Province ?? "";
            set => _settings.Province = value;
        }
        public string District
        {
            get => _settings.District ?? "";
            set => _settings.District = value;
        }
    }

    /// <summary>
    /// The Bloom Library page's values.
    /// </summary>
    public class BloomLibraryValues
    {
        private readonly CollectionSettings _settings;

        public BloomLibraryValues(CollectionSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// The url key of the bookshelf that books uploaded from this collection go into, or ""
        /// for none. While the subscription is expired this is "" and the collection remembers the
        /// shelf in ExpiredBookshelf; CollectionSettingsUpdater.ApplySubscriptionAndBookshelf
        /// keeps that shelf in the file when it is saved. Open books carry the bookshelf (in their
        /// body attributes and branding), so a change needs a restart.
        /// </summary>
        [RequiresRestart]
        public string DefaultBookshelf
        {
            get => _settings.DefaultBookshelf;
            set => _settings.DefaultBookshelf = value;
        }
    }

    /// <summary>
    /// The Advanced page's values.
    /// </summary>
    public class AdvancedValues
    {
        private readonly CollectionSettings _settings;
        private readonly Action<string> _queueRenameOfCollection;

        public AdvancedValues(CollectionSettings settings, Action<string> queueRenameOfCollection)
        {
            _settings = settings;
            _queueRenameOfCollection = queueRenameOfCollection;
        }

        /// <summary>
        /// A user-level setting (Settings.Default.AutoUpdate), not part of the collection.
        /// </summary>
        public bool AutoUpdate
        {
            get =>
                CollectionSettingsDialog.AutoUpdateSupportedOnThisPlatform
                && Settings.Default.AutoUpdate;
            set =>
                Settings.Default.AutoUpdate =
                    value && CollectionSettingsDialog.AutoUpdateSupportedOnThisPlatform;
        }

        /// <summary>
        /// Renaming moves the collection's folder, which happens as the collection reopens, so
        /// a new name is queued rather than set.
        /// </summary>
        [RequiresRestart]
        public string CollectionName
        {
            get => _settings.CollectionName;
            set
            {
                if (value != _settings.CollectionName)
                    _queueRenameOfCollection(value.SanitizeFilename('-'));
            }
        }
    }

    /// <summary>
    /// The experimental features the dialog offers, keyed by their ExperimentalFeatures token.
    /// These are user-level settings, not part of the collection.
    /// </summary>
    public class ExperimentalValues
    {
        [JsonProperty(ExperimentalFeatures.kTeamCollections)]
        [RequiresRestart]
        public bool TeamCollections
        {
            get => ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kTeamCollections);
            set => ExperimentalFeatures.SetValue(ExperimentalFeatures.kTeamCollections, value);
        }
    }

    /// <summary>
    /// The body of POST collection/settings. The dialog works out RestartRequired from the
    /// restart paths, which it needs anyway to label OK. Values stays raw JSON, because it is
    /// populated into a CollectionSettingsValues bound to the open collection.
    /// </summary>
    public class CollectionSettingsSaveRequest
    {
        public JObject Values;
        public bool RestartRequired;
    }
}
