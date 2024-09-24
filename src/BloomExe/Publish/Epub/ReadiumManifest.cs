using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Bloom.SafeXml;
using Newtonsoft.Json;
using SIL.IO;
using SIL.Xml;

// ReSharper disable InconsistentNaming

namespace Bloom.Publish.Epub
{
    /// <summary>
    /// Class to manage the process of making a Readium2 manifest for our Epub previewer.
    /// This is deliberately a minimal manifest to make the viewer work; there's a lot more that
    /// it could contain, especially metadata.
    /// </summary>
    public class ReadiumManifest
    {
        private static ReadiumManifestRoot _manifest;
        private static XmlNamespaceManager _ns;
        private static string _outputPath;
        private static SafeXmlDocument _opfDoc;
        private static string _rootFolderPath;
        private static string _contentPath; // rootfolder/content

        public static string MakeReadiumManifest(string rootFolderPath)
        {
            _rootFolderPath = rootFolderPath;
            _contentPath = Path.Combine(_rootFolderPath, "content");
            var opfPath = Path.Combine(_contentPath, "content.opf");
            var opfData = RobustFile.ReadAllText(opfPath, Encoding.UTF8);
            _opfDoc = SafeXmlDocument.Create();
            _opfDoc.LoadXml(opfData);

            _manifest = new ReadiumManifestRoot();
            _outputPath = Path.Combine(_rootFolderPath, "manifest.json");

            _ns = new XmlNamespaceManager(new NameTable());
            _ns.AddNamespace("dc", "http://purl.org/dc/elements/1.1/");
            _ns.AddNamespace("smil", "http://www.w3.org/ns/SMIL");
            _ns.AddNamespace("opf", "http://www.idpf.org/2007/opf");

            _manifest.type = "application/webpub+json";
            _manifest.title = _opfDoc.SelectSingleNode("//dc:title", _ns)?.InnerText;

            MakeLinks();

            MakeMetaData();

            MakeReadingOrder();

            MakeTOC();

            var output = JsonConvert.SerializeObject(
                _manifest,
                Newtonsoft.Json.Formatting.Indented,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
            );
            RobustFile.WriteAllText(_outputPath, output);
            return _outputPath;
        }

        private static void MakeTOC()
        {
            // We don't have the reader configured to show the TOC, so it doesn't matter much what we put in it,
            // but Readium crashes if we don't have one at all.
            var tocItem = new ReadiumTocItem() { title = "Front Cover", href = "content/1.xhtml" };
            _manifest.toc = new[] { tocItem };
        }

        private static void MakeMetaData()
        {
            var readiumMetadata = new ReadiumMetadata();
            _manifest.metadata = readiumMetadata;

            var renditionElt = _opfDoc.SelectSingleNode(
                "//opf:meta[@property='rendition:layout']",
                _ns
            );
            // Not sure what it should be otherwise...probably flowable? Anyway that is presumably the default
            // So I think we can just leave it out otherwise.
            if (renditionElt?.InnerText == "pre-paginated")
                _manifest.metadata.rendition = new ReadiumRendition() { layout = "fixed" };

            var mediaElt = _opfDoc.SelectSingleNode(
                "//opf:meta[@property='media:active-class']",
                _ns
            );
            if (!string.IsNullOrEmpty(mediaElt?.InnerText))
                _manifest.metadata.MediaOverlay = new ReadiumMediaProps()
                {
                    ActiveClass = mediaElt.InnerText
                };
        }

        private static void MakeReadingOrder()
        {
            var itemElts = _opfDoc.SafeSelectNodes("//manifest/item");
            var itemDict = new Dictionary<string, SafeXmlElement>();
            foreach (SafeXmlElement item in itemElts)
            {
                itemDict[item.GetAttribute("id")] = item;
            }

            var spineElts = _opfDoc.SafeSelectNodes("//spine/itemref").Cast<SafeXmlElement>();
            _manifest.readingOrder = spineElts
                .Select(spineElt =>
                {
                    var spineManifestItem = itemDict[spineElt.GetAttribute("idref")];
                    var roItem = new ReadiumItem()
                    {
                        type = "application/xhtml+xml",
                        href = "content/" + spineManifestItem.GetAttribute("href")
                    };
                    var mediaOverlayId = spineManifestItem.GetAttribute("media-overlay");
                    if (!string.IsNullOrEmpty(mediaOverlayId))
                    {
                        var overlayElt = itemDict[mediaOverlayId];
                        var overlayFileName = overlayElt.GetAttribute("href");
                        var overlayPath = Path.Combine(_contentPath, overlayFileName);
                        // Typically, something like 2_overlay.smil becomes "2".
                        var namePrefix = Path.GetFileNameWithoutExtension(overlayFileName)
                            .Replace("_overlay", "");
                        var readiumMediaName = Path.ChangeExtension(
                            namePrefix + "-media-overlay",
                            "json"
                        );
                        roItem.properties = new ReadiumProperty()
                        {
                            MediaOverlay = readiumMediaName
                        };
                        var smilContent = RobustFile.ReadAllText(overlayPath, Encoding.UTF8);
                        var smilDoc = SafeXmlDocument.Create();
                        smilDoc.LoadXml(smilContent);
                        var seqElt = smilDoc.SelectSingleNode("//smil:seq", _ns) as SafeXmlElement;
                        var textRef = seqElt.GetAttribute(
                            "textref",
                            "http://www.idpf.org/2007/ops"
                        );

                        var readiumOverlay = new ReadiumMediaOverlay();

                        var parElts = smilDoc
                            .SafeSelectNodes("//par")
                            .Cast<SafeXmlElement>()
                            .ToArray();
                        var maxClipEnd = 0m;
                        var narrations = new ReadiumInnerNarrationBlock[parElts.Length];
                        readiumOverlay.role = "section";
                        readiumOverlay.narration = new ReadiumOuterNarrationBlock[1];
                        readiumOverlay.narration[0] = new ReadiumOuterNarrationBlock()
                        {
                            role = new[] { "section", "bodymatter", "chapter" },
                            text = "content/" + textRef,
                            narration = narrations
                        };
                        for (int i = 0; i < parElts.Length; i++)
                        {
                            var parElt = parElts[i];
                            var audioElt = parElt
                                .GetElementsByTagName("audio")
                                .Cast<SafeXmlElement>()
                                .First();
                            var clipEnd = TimeToDecimal(audioElt.GetAttribute("clipEnd"));
                            maxClipEnd = Math.Max(maxClipEnd, clipEnd);
                            var clipStart = TimeToDecimal(audioElt.GetAttribute("clipBegin"));
                            narrations[i] = new ReadiumInnerNarrationBlock()
                            {
                                text =
                                    "content/"
                                    + parElt.GetElementsByTagName("text")[0].GetAttribute("src"),
                                audio =
                                    "content/"
                                    + parElt.GetElementsByTagName("audio")[0].GetAttribute("src")
                                    + "#t="
                                    + clipStart.ToString(CultureInfo.InvariantCulture)
                                    + ","
                                    + clipEnd.ToString(CultureInfo.InvariantCulture)
                            };
                        }

                        RobustFile.WriteAllText(
                            Path.Combine(_rootFolderPath, readiumMediaName),
                            JsonConvert.SerializeObject(readiumOverlay)
                        );

                        roItem.duration = maxClipEnd.ToString(CultureInfo.InvariantCulture);
                    }

                    return roItem;
                })
                .ToArray();
        }

        private static void MakeLinks()
        {
            var link1 = new ReadiumLink()
            {
                type = "application/webpub+json",
                rel = "self",
                href = _outputPath.ToLocalhost()
            };
            // The manifest that R2D2BC generated had another link (probably only for books with media overlays)
            //{
            //	"type": "application/vnd.syncnarr+json",
            //	"templated": true,
            //	"rel": "media-overlay",
            //	"href": "media-overlay.json?resource={path}"
            //}
            // It doesn't seem to be needed for things to work, and I'm not sure what should be
            // in the href field, so I'm just leaving it out.
            _manifest.links = new[] { link1 };
        }

        private static decimal TimeToDecimal(string timeString)
        {
            var parts = timeString.Split(':');
            var time = Decimal.Parse(parts[parts.Length - 1], CultureInfo.InvariantCulture);
            if (parts.Length > 1)
            {
                time += int.Parse(parts[parts.Length - 2]) * 60;
                if (parts.Length > 1)
                {
                    time += int.Parse(parts[parts.Length - 3]) * 3600;
                }
            }

            return time;
        }
    }

    // Object that can be serialized into required manifest.json file for Readium 2.
    public class ReadiumManifestRoot
    {
        public string type;

        public string title;

        public ReadiumLink[] links;

        public ReadiumItem[] readingOrder;

        public ReadiumTocItem[] toc;

        public ReadiumMetadata metadata;
    }

    public class ReadiumMetadata
    {
        public ReadiumRendition rendition;

        [JsonProperty("media-overlay")]
        public ReadiumMediaProps MediaOverlay;
    }

    public class ReadiumLink
    {
        public string type;
        public string rel;
        public string href;
    }

    public class ReadiumItem
    {
        public string type;
        public string href;
        public string duration;
        public ReadiumProperty properties;
    }

    public class ReadiumProperty
    {
        [JsonProperty("media-overlay")]
        public string MediaOverlay;
    }

    public class ReadiumRendition
    {
        public string layout;
    }

    public class ReadiumMediaProps
    {
        [JsonProperty("active-class")]
        public string ActiveClass;
    }

    public class ReadiumTocItem
    {
        public string title;
        public string href;
    }

    /// <summary>
    /// The root item for a Readium media-overlay.json file.
    /// </summary>
    public class ReadiumMediaOverlay
    {
        public string role;
        public ReadiumOuterNarrationBlock[] narration;
    }

    public class ReadiumOuterNarrationBlock
    {
        public string text;
        public string[] role;
        public ReadiumInnerNarrationBlock[] narration;
    }

    public class ReadiumInnerNarrationBlock
    {
        public string text;
        public string audio;
    }
}
