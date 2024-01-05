using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Navigation;
using Amazon.Auth.AccessControlPolicy;
using Bloom;
using Bloom.Book;
using Bloom.web.controllers;
using Newtonsoft.Json;
using SIL.Code;
using SIL.Extensions;
using SIL.IO;
using SIL.Reporting;

/// <summary>
/// This class manages the appearance settings for a book. This includes generating the appearance.css file that is used at display time.
/// It also has responsibilities related to detecting and migrating problem legacy custom css files.
/// </summary>
public class AppearanceSettings
{
    public AppearanceSettings()
    {
        _properties = new ExpandoObject();
        if (_substitutinator == null)
        {
            _substitutinator = new AppearanceCustomCssToThemeSubstitutinator();
        }

        // copy in the default values from each definition
        foreach (var definition in propertyDefinitions)
        {
            definition.SetDefault(_properties); // this is a workaround using ref because c# doesn't have union types, or `any`, sigh
        }
    }

    public static string kDoShowValueForDisplay = "doShow-css-will-ignore-this-and-use-default"; // by using an illegal value, we just get a no-op rule, which is what we want
    public static string kHideValueForDisplay = "none";
    public static string kOverrideGroupsArrayKey = "groupsToOverrideFromParent"; // e.g. "coverFields, xmatter"
    private static AppearanceCustomCssToThemeSubstitutinator _substitutinator;

    // A representation of the content of Appearance.json
    internal dynamic _properties;

    // Review: not sure we need to keep this. There is some danger in having multiple AppearanceSettings objects for the same book.
    // If one has received the Initialize call, but another one (not initialized) is used, we can get wrong answers.
    // But we don't have access to the needed information to do the full Initialize() in the constructor (and can't have, because
    // it comes from BookStorage, whose constructor needs a BookInfo). So we are trying to arrange things so that
    // BookInfos (and AppearanceSettings) that are part of the structure of a book or a BookCollection are so created
    // and used that we only have one per book folder. BookInfos that are created for other purposes, mostly
    // for brief use accessing one property of the metadata, are created without an AppearanceSettings.
    // To help catch any remaining problems, in Debug builds we write to output if we detect multiple AppearanceSettings for the same
    // book folder. Such an event isn't necessarily a bug, but it's worth looking into whether there is danger of
    // using uninitialized appearance settings, or whether a BookInfo with NO appearance settings could be used
    static Dictionary<string, AppearanceSettings> _instances = new();

    // it's a big hassle working directly with the ExpandoObject. By casting it this way, you can do the things you expect.
    internal IDictionary<string, object> Properties => (IDictionary<string, object>)_properties;

    public dynamic TestOnlyPropertiesAccess
    {
        get { return _properties; }
    }

    private string _firstPossiblyOffendingCssFile;
    public string FirstPossiblyOffendingCssFile
    {
        get
        {
            Debug.Assert(
                IsInitialized,
                "Trying to get property of AppearanceSettings that requires Initialize, but it has not been called."
            );
            return _firstPossiblyOffendingCssFile;
        }
        set { _firstPossiblyOffendingCssFile = value; }
    }

    /// <summary>
    /// If this becomes public, it should probably have a check like FirstPossiblyOffendingCssFile
    /// Currently it is only used for logging.
    /// </summary>
    private string OffendingCssRule;

    // Instance is typically created using UpdateFromFolder, which will set this to true if it found and loaded
    // an existing appearance.json file.
    // It is therefore false if the books is, or was created from, a book created by an earlier version of Bloom
    // that does not use the appearance system. Such books are forced into the legacy-5-6 theme, except possibly
    // when deriving a new book from them.
    // It can also be set when WriteToFolder() updates the appearance files to be consistent with the current settings.
    // This only gets called if Bloom is allowed to make changes to the folder, so it remains false
    // if we are loading a legacy book in a folder we can't write.
    private bool _areSettingsConsistentWithFiles;
    public bool IsInitialized { get; private set; }

    // create an array of properties and fill it in
    private PropertyDef[] propertyDefinitions = new PropertyDef[]
    {
        // this one is special because it doesn't correspond to a CSS variable. Instead, we will copy the contents of named file as rules at the end of the CSS file.
        // The default here is rarely if ever relevant. Usually a newly created instance will be initialized from a folder, and the default will be overwritten,
        // either to whatever we find in appearance.json, or to "legacy-5-6" if there is no appearance.json.
        new StringPropertyDef("cssThemeName", "default", "cssThemeName"),
        // Todo: when we implement this setting, we want to migrate the old record of color color.
        // See code commented out in BringBookUpToDateUnprotected.
        //new CssStringVariableDef("coverColor","yellow","colors"),
        new CssDisplayVariableDef("coverShowTitleL2", true, "coverFields"),
        new CssDisplayVariableDef("coverShowTitleL3", false, "coverFields"),
        new CssDisplayVariableDef("coverShowTopic", true, "coverFields"),
        new CssDisplayVariableDef("coverShowLanguageName", false, "coverFields"),
    };

    /// <summary>
    /// Note, we might not actually use this theme at runtime. If the book has css that is incompatible with the new system, we will use legacy-5-6 instead.
    /// </summary>
    public string CssThemeName
    {
        get { return _properties.cssThemeName; }
        set { _properties.cssThemeName = value; }
    }

    /// <summary>
    /// Are we going to use the legacy theme? We MUST use it if we haven't been able to write out a consistent appearance.css file yet.
    /// Otherwise, it generally depends on whether the user selected it, though when we first see a book we may decide to force it.
    /// Review: an alternative is to put the appropriate CSS for the chosen theme into the Book supporting files cache.
    /// But for now we decided on simple: migration to the new theme system only really happens when the user makes a new
    /// book in Bloom 5.7 or later.
    /// </summary>
    public bool UsingLegacy => CssThemeName == "legacy-5-6" || !_areSettingsConsistentWithFiles;
    public string BasePageCssName => UsingLegacy ? "basePage-legacy-5-6.css" : "basePage.css";

    // All of these are set (or cleared) by Initialize()
    private bool _customBookStylesAreIncompatibleWithNewSystem;
    private bool _customCollectionStylesAreIncompatibleWithNewSystem;
    private bool _customBookStylesExists;
    private bool _customBookStyles2Exists;
    private bool _customCollectionStylesExists;

    /// <summary>
    /// Considering all factors, should the book have a link to a customBookStyles.css file?
    /// </summary>
    public bool ShouldUseCustomBookStyles
    {
        get
        {
            if (!_customBookStylesExists)
                return false; // Waste to have the link if there is no file
            if (UsingLegacy)
                return true; // We're using the legacy theme, so we need the custom CSS that is part of it
            // New themes generally use customBookStyles2.css (if any), but if we find a compatible customBookStyles.css
            // we will use it also. This eases migration from the old system when there is no incompatibility.
            // Review: alternatively, we could just rename the old file to customBookStyles2.css, but then we have
            // a problem if the new book is opened in an old Bloom.
            return !_customBookStylesAreIncompatibleWithNewSystem;
        }
    }

    public bool ShouldUseCustomBookStyles2
    {
        get
        {
            if (!_customBookStyles2Exists)
                return false; // Waste to have the link if there is no file
            // This one is only for non-legacy themes
            return !UsingLegacy;
        }
    }

    /// <summary>
    /// Considering all factors, should the book have a link to a customCollectionStyles.css file?
    /// </summary>
    public bool ShouldUseCustomCollectionStyles
    {
        get
        {
            if (!_customCollectionStylesExists)
                return false; // Waste to have the link if there is no file
            if (UsingLegacy)
                return true; // We're using the legacy theme, so we need the custom CSS that is part of it
            return !_customCollectionStylesAreIncompatibleWithNewSystem; // Using another theme, we want it only if it is compatible with the new system
        }
    }

    /// <summary>
    /// We want the appearance.css file unless we're in the legacy theme!
    /// </summary>
    public bool ShouldUseAppearanceCss => !UsingLegacy;

    public List<string> AppearanceRelatedCssFiles(bool useLocalCollectionStyles)
    {
        var result = new List<string>();
        if (ShouldUseAppearanceCss)
            result.Add("appearance.css");
        if (ShouldUseCustomBookStyles)
            result.Add("customBookStyles.css");
        if (ShouldUseCustomBookStyles2)
            result.Add("customBookStyles2.css");
        if (ShouldUseCustomCollectionStyles)
            result.Add(BookStorage.RelativePathToCollectionStyles(useLocalCollectionStyles));
        result.Add(BasePageCssName);
        return result;
    }

    /// <summary>
    /// List all the CSS files that AppearanceRelatedCssFiles might ever return
    /// (for use in deleting the ones that it does not currently return).
    /// </summary>
    public static string[] PossibleAppearanceRelatedCssFiles
    {
        get
        {
            return new[]
            {
                "appearance.css",
                "customBookStyles.css",
                "customBookStyles2.css",
                "customCollectionStyles.css",
                "../customCollectionStyles.cs",
                "basePage.css",
                "basePage-legacy-5-6.css"
            };
        }
    }

    /// <summary>
    /// Given a list of CSS files from a new book, typically customBookStyles.css and customCollectionStyles.css,
    /// decide what theme the book should use, and if we need a specialized customBookStyles2.css file,
    /// return a path to the file that should be copied there.
    /// </summary>
    public string GetThemeAndSubstituteCss(Tuple<string, string>[] cssFilesToCheck)
    {
        // This is currently only used when creating a new book. If the source book was already in the Appearance/Theme system,
        // it will have an appearance.json file, and we will stick with whatever theme and related settings
        // the original book had.
        if (_areSettingsConsistentWithFiles)
            return null;
        // Otherwise, cssThemeName will already be set to "legacy-5-6" by UpdateFromFolder, but for new books we want to
        // try to use the default or some substitute them, and only switch back to the legacy theme,
        // if we don't know of a substitute theme and its associated customBookStyles2.css file.
        CssThemeName = "default";
        string substituteTheme = null;

        foreach (var css in cssFilesToCheck.Where(css => !string.IsNullOrWhiteSpace(css.Item2)))
        {
            // This is our first time initializing the appearance system for this book.
            // Decide what to do about its custom css.
            // We may know to substitute a particular theme/CSS combination for this particular custom css.
            // We may force the legacy theme.

            // Note: we can only cope with one substitution, so if the customBookStyles and the customCollectionStyles both have a substitution
            // (and they aren't identical) we just can't substitute at all.
            // So we'll let the second one we encounter, which finds that it is incompatible, cause
            // us to use the legacy theme.
            var substitutionThemeName = _substitutinator.GetThemeThatSubstitutesForCustomCSS(
                css.Item2
            );
            if (
                substitutionThemeName != null
                && (substituteTheme == null || substituteTheme == substitutionThemeName)
            )
            {
                // We may be able to use the substitute theme... don't celebrate just yet... any other css file could still be incompatible
                substituteTheme = substitutionThemeName;
                CssThemeName = substitutionThemeName;
                SIL.Reporting.Logger.WriteEvent($"** Could use {CssThemeName} BasePage and Theme");
                continue;
            }

            // No substitute found, or we already found a different one that needs a substitute. So we need to
            // see if this one is incompatible.
            if (TestCompatibility(css.Item1, css.Item2, out string _))
            {
                // We found a custom CSS we don't have a rule for, or possibly a second custom CSS different from one that already needed a substitution.
                if (substituteTheme != null)
                {
                    // We process customBookStyles first. If we were planning to substitute for it, presumably it is also incompatible.
                    _customBookStylesAreIncompatibleWithNewSystem = true;
                }

                substituteTheme = null;
                // Don't use FirstPossiblyOffendingCssFile here, typically we haven't been Initialized, so it will throw.
                _firstPossiblyOffendingCssFile = _firstPossiblyOffendingCssFile ?? css.Item1;
                CssThemeName = "legacy-5-6";
                SIL.Reporting.Logger.WriteEvent($"** Will use {CssThemeName} BasePage and Theme");
            }
        }

        Debug.WriteLine($"** Will use {CssThemeName}");
        return substituteTheme == null
            ? null
            : Path.Combine(
                BloomFileLocator.GetFolderContainingAppearanceThemeFiles(),
                $"appearance-theme-{substituteTheme}.css"
            );
    }

    /// <summary>
    /// In version 5.7, we greatly simplified and modernized our basePage css. However, existing books that had custom css could rely on the old approach,
    /// specifically for using margins (and possible other things like page nubmer size/location). Therefore we provide a CSS theme that
    /// effectively just gives you the basePage.css that came with 5.6, now named "basePage-legacy-5-6.css".
    /// As part of setting up a book, we initialize this settings object with a list of CSS files that might be in the book folder.
    /// Return true if we found a problem and had to switch to the legacy theme.
    /// Currently we also pass Css files from branding and xmatter, but we will report a problem if one of them isn't compatible.
    /// When we get more confidence that we have migrated all the brandings and xmatters, we can stop passing them in.
    /// </summary>
    public bool Initialize(Tuple<string, string>[] cssFilesToCheck)
    {
        var result = false;
        // in case we are reinitializing, clear out any old state
        FirstPossiblyOffendingCssFile = null;
        _customBookStylesExists = false;
        _customBookStyles2Exists = false;
        _customBookStylesAreIncompatibleWithNewSystem = false;
        _customCollectionStylesAreIncompatibleWithNewSystem = false;
        _customCollectionStylesExists = false;
        IsInitialized = true;
        // We have to slog through all the css files that might be incompatible with the new system.
        // Even if the legacy theme is active, we still need to know if there are any incompatible files,
        // because it affects the bookSettingsDialog UI
        // Note: JohnH had this done in parallel, using a Where and AsParallel(), but that isn't safe, at least without
        // some locking on FirstPossiblyOffendingCssFile. He also had a FirstOrDefault() in there, but because we're setting
        // state on things like _customBookStylesExists and _customBookStylesAreIncompatibleWithNewSystem, we need to check them all.
        foreach (var css in cssFilesToCheck.Where(css => !string.IsNullOrWhiteSpace(css.Item2)))
        {
            if (TestCompatibility(css.Item1, css.Item2, out string offendingCssRule))
            {
                if (FirstPossiblyOffendingCssFile == null)
                {
                    OffendingCssRule = offendingCssRule;
                    FirstPossiblyOffendingCssFile = css.Item1;
                }
                if (!css.Item1.StartsWith("custom"))
                {
                    SIL.Reporting.ErrorReport.NotifyUserOfProblem(
                        "Unexpectedly found a branding or xmatter CSS not compatible with appearance system: "
                            + css.Item1
                            + " problem rule is: "
                            + OffendingCssRule
                    );
                    // This shouldn't happen, but is probably the best way to carry on if we must
                    if (CssThemeName != "legacy-5-6")
                    {
                        CssThemeName = "legacy-5-6";
                        result = true;
                    }
                }
            }

            Debug.WriteLine($"** Will use {CssThemeName}");
        }

        IndicatorInfoApi.NotifyIndicatorInfoChanged(); // In case the UI read the incomplete settings before we got here
        return result;
    }

    /// <summary>
    /// This is a wrapper for MayBeIncompatible that also sets some flags used by other functions.
    /// It has the same arguments and results.
    /// </summary>
    /// <returns></returns>
    bool TestCompatibility(string cssFileName, string cssFileContent, out string offendingRule)
    {
        if (string.IsNullOrEmpty(cssFileContent))
        {
            offendingRule = null;
            return false;
        }
        else
        {
            if (cssFileName == "customBookStyles.css")
                _customBookStylesExists = true;
            else if (cssFileName == "customBookStyles2.css")
                _customBookStyles2Exists = true;
            else if (cssFileName == "customCollectionStyles.css")
                _customCollectionStylesExists = true;
        }
        var result = MayBeIncompatible(cssFileName, cssFileContent, out offendingRule);
        if (cssFileName == "customBookStyles.css")
            _customBookStylesAreIncompatibleWithNewSystem = result;
        else if (result && cssFileName == "customBookStyles2.css")
        {
            SIL.Reporting.ErrorReport.NotifyUserOfProblem(
                new ShowOncePerSessionBasedOnExactMessagePolicy(),
                "The customBookStyles2.css file is incompatible with this version of Bloom. It will probably cause problems."
            );
        }
        else if (cssFileName == "customCollectionStyles.css")
            _customCollectionStylesAreIncompatibleWithNewSystem = result;
        return result;
    }

    internal static bool MayBeIncompatible(string label, string css, out string offendingRule)
    {
        offendingRule = null;

        if (string.IsNullOrEmpty(css))
            return false;

        // note: "AppearanceVersion" uses the version number of Bloom, but isn't intended to increment with each new release of Bloom.
        // E.g., we do not expect to break CSS files very often. So initially we will have the a.v. = 5.7, and maybe the next one
        // will be 6.2.  Note that there is current some discussion of jumping from 5.6 to 6.0 to make it easier to remember which
        // Bloom version changed to the new css system.
        var v =
            Regex.Match(css, @"compatibleWithAppearanceVersion:\s*(\d+(\.\d+)?)")?.Groups[1]?.Value
            ?? "0";

        if (double.TryParse(v, out var appearanceVersion) && appearanceVersion >= 5.7)
            return false; // this is a 5.7+ theme, so it's fine

        // See if the css contains rules that nowadays should be using css variables, and would likely interfere with 5.6 and up
        const string kProbablyWillInterfere =
            @"\.marginBox\s*{[^}]*?"
            + "(?<![-\\w])" // look-behind to prevent matching things like --page-margin-left
            + "(padding[-:]|left:|top:|right:|bottom:|margin[-:]|width:|height:)[^}]*}";
        var match = Regex.Match(css, kProbablyWillInterfere, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            offendingRule = match.Value;
            var s =
                $"** {label} matched regex for a css rule that is potentially incompatible with this version of the default Bloom Css system: \r\n{offendingRule}";
            Debug.WriteLine(s);
            SIL.Reporting.Logger.WriteEvent(s);
            return true;
        }
        else
            return false;
    }

    /// <summary>
    /// Each book gets an "appearance.css" that is a combination of the appearance-theme-default.css and the css from selected appearance theme.
    /// We only write that file, we don't read it (the browser does, of course). This is the path to that file in the book folder.
    /// </summary>
    public static string AppearanceCssPath(string bookFolderPath)
    {
        return bookFolderPath.CombineForPath("appearance.css");
    }

    /// <summary>
    /// Each book has an "appearance.json" file in its folder that contains the settings for that book.
    /// These are not used at display time; at display time, we rely entirely on the appearance.css file.
    /// </summary>
    private static string AppearanceJsonPath(string bookFolderPath)
    {
        return bookFolderPath.CombineForPath("appearance.json");
    }

    /// <summary>
    /// Update the settings that come from the json file.
    /// (Client still needs to call Initialize() to set up the things that depend on it.
    /// It requires arguments we don't have here and can't easily get, and is too expensive
    /// to do every time this is called.)
    /// </summary>
    /// <param name="bookFolderPath"></param>
    /// <returns></returns>
    public void UpdateFromFolder(string bookFolderPath)
    {
        // Review: possibly remove this once we are sure we have no more problems with this
        // For one thing, it's too strong: I'm not sure we should NEVER do this. But I want to study when it happens.
        // As of when I added it in Dec 2023, it doesn't trigger while starting up a simple collection or editing a book.
        // But I would not be surprised if some form of publication or something in TeamCollection triggers it
        // and requires us to remove it (or fix a problem).
        if (_instances.TryGetValue(bookFolderPath, out AppearanceSettings existingSettings))
            if (!Program.RunningUnitTests && existingSettings != this)
            {
                // There is one known case where this happens, when PageTemplateApi
                // creates a template book for a book that is also in a current collection.
                // Others should ideally be investigated to make sure we don't end up with
                // an uninitialized AppearanceSettings for the current book in one of the
                // main BookCollections.
                Debug.WriteLine("Duplicate bookInfo created for " + bookFolderPath);
                Debug.WriteLine(new Exception().StackTrace);
            }
            else
            {
                _instances[bookFolderPath] = this;
            }
        var jsonPath = AppearanceJsonPath(bookFolderPath);

        if (RobustFile.Exists(jsonPath))
        {
            // We can't actually be absolutely sure that appearance.css is up to date with the json file;
            // we'll risk assuming it is.
            _areSettingsConsistentWithFiles = true;

            var json = RobustFile.ReadAllText(jsonPath);
            UpdateFromJson(json);
            Debug.WriteLine(
                $"Found existing appearance json. CssThemeName is currently {CssThemeName}"
            );
        }
        else
        {
            // if we don't have a json file, we'll switch to legacy. This means we're in a book that has never been in
            // the appearance system (never migrated to 5.7). We switch new books to the default theme if it's safe,
            // but we don't think it's safe to switch existing books. If we decide to allow that after all, note
            // that there are complications if we do so for a book where we can't write out appearance.css.
            // It should be possible in such a case to push it into the BookStorage's _supportingFiles cache.
            CssThemeName = "legacy-5-6";
        }
    }

    public void AllowLaterInstance(string bookFolderPath)
    {
        _instances.Remove(bookFolderPath);
    }

    /// <summary>
    /// Create something like this:
    /// ":root{
    ///		--coverShowTitleL2: bogus-value-so-default-is-used;
    ///		--coverShowTitleL3: none;
    ///		--coverShowTopic: bogus-value-so-default-is-used;
    ///		--coverShowLanguageName: none;
    ///	}"
    /// </summary>
    public string GetCssRootDeclaration(AppearanceSettings parent = null)
    {
        var cssBuilder = new StringBuilder();
        cssBuilder.AppendLine(":root{");
        var overrides = Properties.ContainsKey(kOverrideGroupsArrayKey)
            ? (System.Collections.Generic.List<object>)Properties[kOverrideGroupsArrayKey]
            : null;

        //foreach (var property in _properties.Properties())
        foreach (var property in (IDictionary<string, object>)_properties)
        {
            if (property.Key == kOverrideGroupsArrayKey)
                continue;
            var definition = propertyDefinitions.FirstOrDefault(d => d.Name == property.Key);
            if (definition == null)
            {
                // Note that we intentionally don't *remove* the property, because maybe someone set it using a later version of Bloom.
                NonFatalProblem.Report(
                    ModalIf.None,
                    PassiveIf.Alpha,
                    $"Unexpected field {property.Key}",
                    $"appearance.json has a field that this version of Bloom does not have a definition for: {property.Key}. This is not necessarily a problem."
                );
            }

            var keyValuePair = property; // start by saying the value is whatever the child has
            if (parent != null) // then if there is a parent...
            {
                // and this property is part of an override group...
                if (!string.IsNullOrEmpty(definition.OverrideGroup))
                {
                    // if _properties has a value for kOverrideGroupListPropertyKey
                    if (Properties.ContainsKey(kOverrideGroupsArrayKey))
                    {
                        // but that group is not listed as something to override...
                        if (overrides == null || !overrides.Contains(definition.OverrideGroup))
                        {
                            if (
                                !((IDictionary<string, object>)parent._properties).ContainsKey(
                                    property.Key
                                )
                            )
                            {
                                Debug.Assert(
                                    false,
                                    $"The property '{property.Key}' is defined as part of the override group '{definition.OverrideGroup}', but the parent does not have a value for it."
                                );
                                SIL.Reporting.Logger.WriteMinorEvent(
                                    $"Warning: The property '{property.Key}' is defined as part of the override group '{definition.OverrideGroup}', but the parent does not have a value for it."
                                );
                                // we'll just use the child's value
                            }
                            else
                            {
                                // OK, the property is not part of a group that is overridden & the parent has a value, so use the parent's value.
                                var parentValue = ((IDictionary<string, object>)parent._properties)[
                                    property.Key
                                ];
                                keyValuePair = new KeyValuePair<string, object>(
                                    property.Key,
                                    parentValue
                                );
                            }
                        }
                    }
                }
            }

            if (definition is CssPropertyDef)
                cssBuilder.AppendLine(
                    ((PropertyDef)definition).GetCssVariableDeclaration(keyValuePair)
                );
        }

        cssBuilder.AppendLine("}");
        return cssBuilder.ToString();
    }

    /// <summary>
    /// Save both the css and the json version of the settings to the book folder.
    /// </summary>
    public void WriteToFolder(string folder)
    {
        var targetPath = AppearanceCssPath(folder);

        if (Program.RunningHarvesterMode && RobustFile.Exists(targetPath))
        {
            // Would overwrite, but overwrite not allowed in Harvester mode.
            // Review: (this logic just copied from CreateOrUpdateDefaultLangStyles() above, I don't know if it's still valid)
            return;
        }

        var cssBuilder = new StringBuilder();

        // Add in all the user's settings
        cssBuilder.AppendLine("/* From this book's appearance settings */");
        cssBuilder.AppendLine(GetCssRootDeclaration(null));

        var theme = CssThemeName;

        if (!theme.StartsWith("legacy"))
        {
            // Add in the var declarations of the default, so that the display doesn't collapse just because a theme is missing
            // some var that basepage.css relies on.

            var defaultThemeSourcePath = Path.Combine(
                BloomFileLocator.GetFolderContainingAppearanceThemeFiles(),
                "appearance-theme-default.css"
            );
            cssBuilder.AppendLine("/* From appearance-theme-default.css */");
            cssBuilder.AppendLine(RobustFile.ReadAllText(defaultThemeSourcePath, Encoding.UTF8));
        }

        // Now add the user's chosen theme if it isn't the default, which we already added above.
        if (!string.IsNullOrEmpty(theme) && theme != "default")
        {
            var sourcePath = Path.Combine(
                BloomFileLocator.GetFolderContainingAppearanceThemeFiles(),
                $"appearance-theme-{theme}.css"
            );
            if (!RobustFile.Exists(sourcePath))
            {
                // TODO: We should toast I suppose?
            }
            else
            {
                cssBuilder.AppendLine(
                    $"/* The following rules are from the current appearance theme, '{theme}' */"
                );
                cssBuilder.AppendLine(RobustFile.ReadAllText(sourcePath, Encoding.UTF8));
            }
        }

        RobustFile.WriteAllText(targetPath, cssBuilder.ToString());
        var settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
            /* doesn't work: CreateProperty() is never called
              ContractResolver = new PropertiesContractResolver()
            */
        };

        var s = JsonConvert.SerializeObject(_properties, settings);

        RobustFile.WriteAllText(AppearanceJsonPath(folder), s);
        _areSettingsConsistentWithFiles = true;
    }

    /// <summary>
    /// Read in our settings from JSON (typically from appearance.json in the book folder)
    /// </summary>
    internal void UpdateFromJson(string json)
    {
        // parse the json into an object
        var x = JsonConvert.DeserializeObject<ExpandoObject>(json);
        //and then for each property, copy into the _properties object
        // For backwards capabilty, if the json we are reading has a null for a value,
        // do not override the default value that we already have loaded.
        foreach (var property in (IDictionary<string, object>)x)
        {
            Properties[property.Key] = property.Value;
        }
    }

    /// <summary>
    /// Read in our settings from a dynamic object (typically from Configr in the BookSettings dialog)
    /// </summary>
    internal void UpdateFromDynamic(Newtonsoft.Json.Linq.JObject replacement)
    {
        foreach (var property in replacement)
        {
            ((IDictionary<string, object>)_properties)[property.Key] = property.Value;
        }
    }

    public static IEnumerable<string> GetPathsToThemeFiles()
    {
        return Directory.EnumerateFiles(
            BloomFileLocator.GetFolderContainingAppearanceThemeFiles(),
            "*.css"
        );
    }

    public static IEnumerable<string> GetAppearanceThemeFileNames()
    {
        string[] themes = { };
        RetryUtility.Retry(() =>
        {
            var x =
                from f in Directory.EnumerateFiles(
                    BloomFileLocator.GetFolderContainingAppearanceThemeFiles(),
                    "*.css"
                )
                select Path.GetFileNameWithoutExtension(f);
            themes = x.ToArray();
        });
        return themes;
    }

    static IEnumerable<string> GetAppearanceThemeNames()
    {
        return from path in GetAppearanceThemeFileNames()
            select Path.GetFileName(path).Replace("appearance-theme-", "");
    }

    // things that aren't settings but are used by the BookSettings UI
    public string AppearanceUIOptions
    {
        get
        {
            var names = GetAppearanceThemeNames();
            var x = new ExpandoObject() as IDictionary<string, object>;

            x["themeNames"] =
                from name in names.ToArray<string>()
                select new { label = name, value = name };
            x["firstPossiblyLegacyCss"] = FirstPossiblyOffendingCssFile;
            return JsonConvert.SerializeObject(x);
        }
    }

    public object ChangeableSettingsForUI
    {
        get { return _properties; }
    }
}

abstract class PropertyDef
{
    public string Name;
    public dynamic DefaultValue;

    public void SetDefault(dynamic prop)
    {
        ((IDictionary<string, object>)prop)[Name] = DefaultValue;
    }

    /// <summary>
    /// The name of the group of properties that can a book can override from a collection, or a page can override from a book.
    /// </summary>
    public string OverrideGroup;

    public abstract string GetCssVariableDeclaration(dynamic property);
}

abstract class CssPropertyDef : PropertyDef { }

class StringPropertyDef : PropertyDef
{
    public StringPropertyDef(string name, string defaultValue, string overrideGroup)
    {
        Name = name;
        DefaultValue = defaultValue;
        OverrideGroup = overrideGroup;
    }

    public override string GetCssVariableDeclaration(dynamic property)
    {
        return $"--{Name}: {property.Value};";
    }
}

class CssStringVariableDef : CssPropertyDef
{
    public CssStringVariableDef(string name, string defaultValue, string overrideGroup)
    {
        Name = name;
        DefaultValue = defaultValue;
        OverrideGroup = overrideGroup;
    }

    public override string GetCssVariableDeclaration(dynamic property)
    {
        return $"--{Name}: {property.Value};";
    }
}

/// <summary>
/// variables that can be used in rules like ` .something { display: var(--coverShowTopic) }`
/// </summary>
class CssDisplayVariableDef : CssPropertyDef
{
    public string TrueValue;
    public string FalseValue;

    public CssDisplayVariableDef(string name, bool defaultValue, string overrideGroup)
    {
        Name = name;
        TrueValue = AppearanceSettings.kDoShowValueForDisplay; // by using an illegal value, we just get a no-op rule, which is what we want
        FalseValue = AppearanceSettings.kHideValueForDisplay;
        DefaultValue = defaultValue;
        OverrideGroup = overrideGroup;
    }

    public override string GetCssVariableDeclaration(dynamic property)
    {
        var value = (bool)property.Value ? TrueValue : FalseValue;
        return $"--{Name}: {value};";
    }
}
