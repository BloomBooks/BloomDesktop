using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Bloom.SafeXml;

namespace Bloom.Spreadsheet
{
    /// <summary>
    /// A kind of thing on a page that the ordinary [page content] machinery cannot carry,
    /// because putting it back needs more than the text, image, video and widget columns
    /// hold. Such a thing gets rows of its own: a lead row with a row label of its own,
    /// whose hidden [details] cell holds a JSON object whose "kind" property is this
    /// kind's <see cref="Kind"/>, optionally followed by more rows that belong to the
    /// same thing.
    ///
    /// Registering a kind (see <see cref="SpreadsheetObjectKinds.Register"/>) is all it
    /// takes to make the exporter and importer handle those rows: export asks each
    /// registered kind what the page holds and writes each object's rows at the object's
    /// own position among the page's [page content] rows, and import collects each lead
    /// row's family, finds the object on the target page, and hands both to the kind.
    /// </summary>
    public interface ISpreadsheetObjectKind
    {
        /// <summary>
        /// The value the "kind" property of the [details] JSON carries for this kind's
        /// rows. It identifies a [details] cell even apart from the row it sits in.
        /// </summary>
        string Kind { get; }

        /// <summary>
        /// The row label of the lead row of one object of this kind, in the bracketed form
        /// every row label takes. No two registered kinds may share one.
        /// </summary>
        string LeadRowLabel { get; }

        /// <summary>
        /// True if this row continues the family of rows led by a <see cref="LeadRowLabel"/>
        /// row of this kind, rather than starting something else. Asked of the rows after a
        /// lead row, in order, and of no more rows once it has answered false.
        /// </summary>
        bool IsContinuationRow(ContentRow row);

        /// <summary>
        /// The objects of this kind that the page holds in its own right, in document
        /// order. Objects nested inside another object of this kind are not included: they
        /// belong to the object that holds them, not to the page. A kind whose objects hang
        /// off something else rather than sitting on the page returns an empty list, and
        /// then it is up to the kind to get its rows written and read.
        /// </summary>
        List<SafeXmlElement> GetObjectsOnPage(SafeXmlElement page);

        /// <summary>
        /// True if the element is inside one of this kind's objects, in which case it
        /// belongs to that object rather than to the page: the exporter's and importer's
        /// generic collectors of translation groups, bloom-canvases, video containers and
        /// widget containers all leave such elements alone, since the object's own rows
        /// carry them.
        /// </summary>
        bool IsInsideObject(SafeXmlElement element);

        /// <summary>
        /// Writes the rows for one of the objects <see cref="GetObjectsOnPage"/> returned.
        /// The first row written must have <see cref="LeadRowLabel"/> in its row-type cell
        /// and a [details] cell whose JSON says "kind": <see cref="Kind"/>. The [details]
        /// column already exists by the time this is called.
        /// </summary>
        void ExportObject(SafeXmlElement obj, SpreadsheetObjectExportContext context);

        /// <summary>
        /// Puts one family of rows (its lead row first, then whatever
        /// <see cref="IsContinuationRow"/> claimed) back into the book, into or in place of
        /// <see cref="SpreadsheetObjectImportContext.TargetElement"/>.
        /// </summary>
        Task ImportObjectAsync(List<ContentRow> rows, SpreadsheetObjectImportContext context);
    }

    /// <summary>
    /// What <see cref="ISpreadsheetObjectKind.ExportObject"/> needs beyond the object
    /// itself. Anything else it wants is on the <see cref="Exporter"/>.
    /// </summary>
    public class SpreadsheetObjectExportContext
    {
        /// <summary>The exporter doing this export.</summary>
        public SpreadsheetExporter Exporter { get; set; }

        /// <summary>The spreadsheet being built; make rows with <c>new ContentRow(this)</c>.</summary>
        public InternalSpreadsheet Spreadsheet { get; set; }

        /// <summary>The page number to put in the [page number] cell of each row made.</summary>
        public string PageNumber { get; set; }

        /// <summary>
        /// The background color of this page's rows. Rows made for an object should get it
        /// too, since they are part of the export of the same chunk of the document.
        /// </summary>
        public Color ColorForPage { get; set; }

        /// <summary>The folder of the book being exported, for resolving image and video paths.</summary>
        public string BookFolderPath { get; set; }

        /// <summary>
        /// Call this with the first row made for the object. If the page's type has not
        /// been written yet it goes on that row; otherwise this does nothing. Export puts
        /// the page type on the first row it makes for a page, whatever kind of row it is,
        /// so that later rows can go onto the same page if there is room.
        /// </summary>
        public Action<ContentRow> SetPageTypeIfNeeded { get; set; }
    }

    /// <summary>
    /// What <see cref="ISpreadsheetObjectKind.ImportObjectAsync"/> needs beyond the rows.
    /// Anything else it wants is on the <see cref="Importer"/>.
    /// </summary>
    public class SpreadsheetObjectImportContext
    {
        /// <summary>The importer doing this import.</summary>
        public SpreadsheetImporter Importer { get; set; }

        /// <summary>The spreadsheet being imported.</summary>
        public InternalSpreadsheet Spreadsheet { get; set; }

        /// <summary>
        /// The object on the target page that this family of rows is for: the next object
        /// of this kind on the page the rows belong to, found the same way a [page content]
        /// row finds its translation group. Never null; import reports and skips a family
        /// for which no object could be found rather than calling the kind.
        /// </summary>
        public SafeXmlElement TargetElement { get; set; }

        /// <summary>Reports a problem to the user. The message should name the row it is about.</summary>
        public Action<string> Warn { get; set; }

        /// <summary>
        /// Say which row of the family is being worked on (0 for the lead row) so that
        /// <see cref="SpreadsheetImporter.CurrentRowIndexForMessages"/> names that row.
        /// </summary>
        public Action<int> SetRowInFamilyBeingProcessed { get; set; }
    }

    /// <summary>
    /// One object found on a page, paired with the kind that owns it, in the form the
    /// exporter needs to place its rows.
    /// </summary>
    public class SpreadsheetObjectOnPage
    {
        /// <summary>The kind that reported this object.</summary>
        public ISpreadsheetObjectKind Kind { get; set; }

        /// <summary>The element on the page.</summary>
        public SafeXmlElement Element { get; set; }
    }

    /// <summary>
    /// The registered <see cref="ISpreadsheetObjectKind"/>s, and the questions the exporter
    /// and importer ask of all of them at once.
    ///
    /// Registration is static because the kinds are fixed features of Bloom rather than
    /// per-book or per-export choices; a kind registers itself once at startup. Tests that
    /// register a kind must <see cref="Unregister"/> it again, or they will change what
    /// every later test in the process sees.
    /// </summary>
    public static class SpreadsheetObjectKinds
    {
        private static readonly List<ISpreadsheetObjectKind> _kinds =
            new List<ISpreadsheetObjectKind>();

        /// <summary>
        /// Makes a kind known to the exporter and importer. Neither its
        /// <see cref="ISpreadsheetObjectKind.LeadRowLabel"/> nor its
        /// <see cref="ISpreadsheetObjectKind.Kind"/> may already be taken: the row label
        /// is what picks the kind for a row, and the kind name is what the row's [details]
        /// cell claims, so two kinds sharing either would be indistinguishable.
        /// </summary>
        public static void Register(ISpreadsheetObjectKind kind)
        {
            if (kind == null)
                throw new ArgumentNullException(nameof(kind));
            if (ForLeadRowLabel(kind.LeadRowLabel) != null)
                throw new ArgumentException(
                    $"A spreadsheet object kind using the row label {kind.LeadRowLabel} is already registered."
                );
            if (_kinds.Any(k => k.Kind == kind.Kind))
                throw new ArgumentException(
                    $"A spreadsheet object kind named {kind.Kind} is already registered."
                );
            _kinds.Add(kind);
        }

        /// <summary>
        /// Forgets a kind. Returns whether it was registered. Mainly for tests.
        /// </summary>
        public static bool Unregister(ISpreadsheetObjectKind kind)
        {
            return _kinds.Remove(kind);
        }

        /// <summary>Every registered kind, in registration order.</summary>
        public static IReadOnlyList<ISpreadsheetObjectKind> All => _kinds;

        /// <summary>
        /// The kind whose lead row carries this row label, or null if no registered kind
        /// does (which is the case for every row label in a spreadsheet made before any
        /// kind existed).
        /// </summary>
        public static ISpreadsheetObjectKind ForLeadRowLabel(string rowLabel)
        {
            return _kinds.FirstOrDefault(k => k.LeadRowLabel == rowLabel);
        }

        /// <summary>
        /// The kind that would have claimed this row as a continuation of one of its
        /// families, or null. Used to tell the user that such a row was stranded away from
        /// the lead row that gives it meaning.
        /// </summary>
        public static ISpreadsheetObjectKind ThatWouldContinueWith(ContentRow row)
        {
            return _kinds.FirstOrDefault(k => k.IsContinuationRow(row));
        }

        /// <summary>
        /// True if the element belongs to an object of some registered kind, so that the
        /// generic collectors of a page's translation groups, bloom-canvases, video
        /// containers and widget containers must leave it alone.
        /// </summary>
        public static bool IsInsideAnObject(SafeXmlElement element)
        {
            // Fast path for the ordinary case of a book with none of these objects in it.
            if (_kinds.Count == 0)
                return false;
            return _kinds.Any(k => k.IsInsideObject(element));
        }

        /// <summary>
        /// Every registered kind's objects on the page, in document order, so that the
        /// exporter can write each object's rows at the object's own position among the
        /// page's [page content] rows.
        /// </summary>
        public static List<SpreadsheetObjectOnPage> ObjectsOnPage(SafeXmlElement page)
        {
            if (_kinds.Count == 0)
                return new List<SpreadsheetObjectOnPage>();
            var found = _kinds
                .SelectMany(k =>
                    k.GetObjectsOnPage(page)
                        .Select(e => new SpreadsheetObjectOnPage { Kind = k, Element = e })
                )
                .ToList();
            if (found.Count < 2)
                return found;
            var documentOrder = GetDocumentOrder(page);
            return found
                .OrderBy(o =>
                    documentOrder.TryGetValue(o.Element, out var order) ? order : int.MaxValue
                )
                .ToList();
        }

        /// <summary>
        /// Every element at or under the page, numbered in document order, so that two
        /// elements found by different searches can be told which comes first.
        /// </summary>
        public static Dictionary<SafeXmlElement, int> GetDocumentOrder(SafeXmlElement page)
        {
            var result = new Dictionary<SafeXmlElement, int>();
            var next = 0;
            void Walk(SafeXmlNode node)
            {
                if (node is SafeXmlElement element)
                    result[element] = next++;
                foreach (var child in node.ChildNodes)
                    Walk(child);
            }
            Walk(page);
            return result;
        }
    }
}
