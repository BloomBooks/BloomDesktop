using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
using Bloom.TeamCollection;
using CommandLine;
using SIL.IO;
using SIL.Progress;

namespace Bloom.CLI
{
    /// <summary>
    /// This command needs to
    /// * set the xmatter to something appropriate
    /// * set the layout to something appropriate
    /// * set the L1, L2, and L3 languages
    /// * Spread all the above around, as if the book had been loaded in Bloom
    /// * Make sure it has all the needed stylesheets
    /// </summary>
    class HydrateBookCommand
    {
        public static Task<int> Handle(HydrateParameters options)
        {
            if (!Directory.Exists(options.Path))
            {
                if (options.Path.Contains(".htm"))
                {
                    Debug.WriteLine("Supply only the directory, not the path to the file.");
                    Console.Error.WriteLine("Supply only the directory, not the path to the file.");
                }
                else
                {
                    Debug.WriteLine("Could not find " + options.Path);
                    Console.Error.WriteLine("Could not find " + options.Path);
                }
                return Task.FromResult(1);
            }
            Console.WriteLine("Starting Hydrating.");

            var layout = new Layout
            {
                SizeAndOrientation = SizeAndOrientation.FromString(options.SizeAndOrientation)
            };

            if (
                RobustFile.Exists(
                    TeamCollectionManager.GetTcLinkPathFromLcPath(
                        Path.GetDirectoryName(options.Path)
                    )
                )
            )
            {
                throw new ApplicationException(
                    "Hydrate command cannot currently be used in Team Collections"
                );
                // To make this possible, we'd need to spin up a TeamCollectionManager and TeamCollection and pass the latter
                // to the Book as its SaveContext and still changes would be forbidden unless the book was checked out.
            }

            var collectionSettings = new CollectionSettings
            {
                XMatterPackName = options.XMatter,
                Language1Tag = options.VernacularTag,
                Language2Tag = string.IsNullOrWhiteSpace(options.NationalLanguage1Tag)
                    ? options.VernacularTag
                    : options.NationalLanguage1Tag,
                Language3Tag = options.NationalLanguage2Tag
            };

            XMatterPackFinder xmatterFinder = new XMatterPackFinder(
                new[] { BloomFileLocator.GetFactoryXMatterDirectory(), }
            );
            var locator = new BloomFileLocator(
                collectionSettings,
                xmatterFinder,
                ProjectContext.GetFactoryFileLocations(),
                ProjectContext.GetFoundFileLocations(),
                ProjectContext.GetAfterXMatterFileLocations()
            );

            // alwaysSaveable is fine here, as we already checked it's not a TC.
            var bookInfo = new BookInfo(options.Path, true, new AlwaysEditSaveContext());
            var book = new Book.Book(
                bookInfo,
                new BookStorage(bookInfo, locator, new BookRenamedEvent(), collectionSettings),
                null,
                collectionSettings,
                null,
                null,
                new BookRefreshEvent(),
                new BookSavedEvent()
            );
            // This was added as part of the phase 1 changes towards the new language system, where book languages
            // are more clearly distinct from collection languages, and there's no sense (except underlying storage) in which
            // a book has languages that are not selected for display. This made it necessary to decide explicitly
            // whether passing the national language options implies that a book is bi- or tri-lingual. Andrew and I (JohnT)
            // could not think of any reason to pass the arguments at all except to achieve that, so I made it so.
            var langs = new List<string>();
            langs.Add(options.VernacularTag);
            if (
                !string.IsNullOrEmpty(options.NationalLanguage1Tag)
                && options.NationalLanguage1Tag != options.VernacularTag
            )
                langs.Add(options.NationalLanguage1Tag);
            if (!string.IsNullOrEmpty(options.NationalLanguage2Tag))
                langs.Add(options.NationalLanguage2Tag);
            book.SetMultilingualContentLanguages(langs.ToArray());

            //we might change this later, or make it optional, but for now, this will prevent surprises to processes
            //running this CLI... the folder name won't change out from under it.
            book.LockDownTheFileAndFolderName = true;

            book.SetLayout(layout);
            book.EnsureUpToDate();
            Console.WriteLine("Finished Hydrating.");
            Debug.WriteLine("Finished Hydrating.");
            return Task.FromResult(0);
        }
    }
}

// Used with https://github.com/gsscoder/commandline, which we get via nuget.
// (using the beta of commandline 2.0, as of Bloom 3.8)

[Verb(
    "hydrate",
    HelpText = "Apply XMatter, Page Size/Orientation, and Languages. Used by automated converters."
)]
public class HydrateParameters
{
    public enum PresetOption
    {
        Shellbook
    }

    private PresetOption _preset;
    private string _sizeAndOrientation;
    private string _xMatter;
    private string _nationalLanguage1Tag;
    private string _nationalLanguage2Tag;

    [Option("bookpath", HelpText = "path to the book", Required = true)]
    public string Path { get; set; }

    // Originally, the idea here was to take an existing book and make an app out of it. However, we
    // no longer support that use case. Leaving this legacy comment here to help us understand the original purpose:
    //
    // When a book is opened in a collection, Bloom gathers the vernacular, national, and regional languages
    // from the collection settings and makes changes to the html so that, for example, the current vernacular
    // shows on each page, rather than in the source bubbles. It does that by adding classes such as "content1".
    // The command being defined here can do that, too. This is needed for cases where, for example, the user
    // selects a book from BloomLibrary and wants to make an app out of it for his language. But his language
    // might not be the one that was "l1" when the book was uploaded. Using these parameters, the program making
    // him an app can specify that this language should be the l1.

    [Option("vernacularisocode", HelpText = "language tag of primary language", Required = true)]
    public string VernacularTag { get; set; }

    [Option(
        "nationallanguage1isocode",
        HelpText = "language tag of secondary language",
        Default = "",
        Required = false
    )]
    public string NationalLanguage1Tag
    {
        //"Default" is not working
        get { return _nationalLanguage1Tag ?? string.Empty; }
        set { _nationalLanguage1Tag = value; }
    }

    [Option(
        "nationallanguage2isocode",
        HelpText = "language tag of tertiary language",
        Default = "",
        Required = false
    )]
    public string NationalLanguage2Tag
    {
        //"Default" is not working
        get { return _nationalLanguage2Tag ?? string.Empty; }
        set { _nationalLanguage2Tag = value; }
    }

    [Option(
        "preset",
        HelpText = "alternative to specifying layout and xmatter. Only current option is 'shellbook'.",
        Required = false
    )]
    public string Preset
    {
        get { return _preset.ToString().ToLowerInvariant(); }
        set
        {
            switch (value.ToLowerInvariant())
            {
                case "shellbook":
                    _preset = PresetOption.Shellbook;
                    SizeAndOrientation = "Device16x9Portrait";
                    XMatter = "Device";
                    break;
                default:
                    throw new ArgumentException(
                        "{0} is not a valid preset. Only current option is 'shellbook'.",
                        value
                    );
            }
        }
    }

    [Option("sizeandorientation", HelpText = "desired size & orientation", Required = false)]
    public string SizeAndOrientation
    {
        get
        {
            if (string.IsNullOrEmpty(_sizeAndOrientation))
            {
                return "Device16x9Landscape";
            }

            return _sizeAndOrientation;
        }
        set { _sizeAndOrientation = value; }
    }

    [Option(
        "xmatter",
        HelpText = "front/back matter pack to apply. E.g. 'Device', 'Factory'",
        Required = false
    )]
    public string XMatter
    {
        get
        {
            if (string.IsNullOrEmpty(_xMatter))
            {
                return "Device";
            }

            return _xMatter;
        }
        set { _xMatter = value; }
    }

    /*
    [Option("multilinguallevel", HelpText = "value of either 1, 2, or 3 (monolingual, bilingual, trilingual)", Required = false)]
    public int MultilingualLevel { get; set; }
    */
}
