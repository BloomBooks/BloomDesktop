using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.SafeXml;
using Newtonsoft.Json.Linq;

namespace Bloom.Spreadsheet
{
    /// <summary>
    /// What the spreadsheet export and import of bloom-table elements have in common: the
    /// class and attribute names of the saved table markup (see the bloom-table library's
    /// README and table-renderer.ts), the list of edit-time artifacts that must not be
    /// written to a spreadsheet, and the default content of a cell of each type.
    ///
    /// A table goes into a spreadsheet verbatim rather than semantically: one JSON object
    /// holding every attribute of the table element and of each of its cells, exactly as
    /// they are written in the book. Nothing here interprets a column width, a border
    /// matrix or an inline grid-template style, so a table that comes back from a
    /// spreadsheet renders the same as the one that went in -- including in a book that is
    /// published without ever being opened in the Edit tab, where the read-time CSS relies
    /// on the inline grid styles the renderer wrote.
    /// </summary>
    public static class SpreadsheetTables
    {
        // The "kind" of the JSON in a [details] cell. The kind comes first in the object,
        // so the blob identifies itself even apart from the row it sits in.
        public const string TableKind = "table";
        public const string TableCellKind = "table-cell";

        public const string TableClass = "bloom-table";
        public const string CellClass = "bloom-cell";

        // A cell that a merge has covered. It stays in the DOM (the cells of a table are
        // always rowCount * columnCount of them, row-major) but shows nothing and holds
        // no content, so it goes into the spreadsheet as attributes only, with no row of
        // its own.
        public const string SkipClass = "bloom-skip";

        public const string ContentTypeAttribute = "data-content-type";
        public const string ColumnWidthsAttribute = "data-column-widths";

        public const string TextContentType = "text";
        public const string ImageContentType = "image";
        public const string VideoContentType = "video";
        public const string TableContentType = "table";

        // Attributes the table editing UI puts on a table or a cell for the duration of an
        // editing session. prepare-for-save.ts strips these before Bloom saves the page;
        // we strip them again here so that a spreadsheet made from a page that somehow
        // still has them does not carry them into another book.
        private static readonly string[] kEditOnlyAttributes = new[]
        {
            "data-table-attached",
            "data-btable-anchor-name",
            "data-table-overlay",
        };

        // Classes that mean "this is the table or cell the user is working on right now".
        private static readonly string[] kEditOnlyClasses = new[]
        {
            "cell--selected",
            "table--selected",
            "bloom-pointer-near",
            "bloom-current-table",
        };

        // The pulse highlight uses several classes (bloom-pulse-fill, bloom-pulse-border);
        // all of them start this way.
        private const string kEditOnlyClassPrefix = "bloom-pulse-";

        // Style properties that only mean something within the session that wrote them:
        // the anchor name minted for a cell so the menu pills can be positioned against
        // it, and the boundary hint colors an older renderer wrote inline.
        private static readonly string[] kEditOnlyStyleProperties = new[]
        {
            "anchor-name",
            "--hint-top-color",
            "--hint-right-color",
            "--hint-bottom-color",
            "--hint-left-color",
        };

        /// <summary>
        /// The default content of a text cell: a translation group, as
        /// ensureContentTypesRegistered in tableEditing.ts makes one, so that text in a
        /// table participates in Bloom's multilingual system and its styles. The lang="z"
        /// editable is the prototype the importer's per-language editables are cloned from.
        /// </summary>
        public const string TextCellContent =
            "<div class=\"bloom-translationGroup bloom-trailingElement normal-style\">"
            + "<div class=\"bloom-editable normal-style\" lang=\"z\" contenteditable=\"true\"><p></p></div>"
            + "</div>";

        /// <summary>
        /// The default content of a picture cell: a bloom-canvas holding a background image
        /// canvas element, so Bloom's image tooling works inside a cell. Same markup as
        /// tableEditing.ts registers, and as origami's Image link creates.
        /// </summary>
        public const string ImageCellContent =
            "<div class=\"bloom-canvas bloom-has-canvas-element bloom-leadingElement\">"
            + "<div class=\"bloom-canvas-element bloom-backgroundImage\" style=\"width:100%;height:100%;\">"
            + "<div class=\"bloom-imageContainer\"><img src=\"placeHolder.png\"></img></div>"
            + "</div></div>";

        /// <summary>
        /// The default content of a video cell. Same markup as tableEditing.ts registers,
        /// and as origami's Video link creates.
        /// </summary>
        public const string VideoCellContent =
            "<div class=\"bloom-videoContainer bloom-leadingElement bloom-noVideoSelected\"></div>";

        /// <summary>
        /// True if the element is a bloom-table.
        /// </summary>
        public static bool IsTable(SafeXmlElement element)
        {
            return element != null && element.HasClass(TableClass);
        }

        /// <summary>
        /// True if the element is inside a bloom-table, and so belongs to that table
        /// rather than to the page. The generic collectors that turn a page's translation
        /// groups, canvases and video containers into [page content] rows must skip these.
        /// </summary>
        public static bool IsInsideTable(SafeXmlElement element)
        {
            for (var parent = element?.ParentNode as SafeXmlElement; parent != null; )
            {
                if (IsTable(parent))
                    return true;
                parent = parent.ParentNode as SafeXmlElement;
            }
            return false;
        }

        /// <summary>
        /// The tables on a page that are not nested inside another table, in document order.
        /// These are the ones that get a [table] row of their own; a nested table's rows
        /// hang off the cell that holds it.
        /// </summary>
        public static List<SafeXmlElement> TopLevelTables(SafeXmlElement pageOrCell)
        {
            return SafeXmlElement
                .GetAllDivsWithClass(pageOrCell, TableClass)
                .Where(table => !IsInsideTable(table))
                .ToList();
        }

        /// <summary>
        /// All the cells of a table, in DOM (row-major) order, including the bloom-skip
        /// ones. Same rule as the library's cellsOf: direct children carrying bloom-cell.
        /// </summary>
        public static List<SafeXmlElement> CellsOf(SafeXmlElement table)
        {
            return table
                .ChildNodes.OfType<SafeXmlElement>()
                .Where(child => child.HasClass(CellClass))
                .ToList();
        }

        /// <summary>
        /// How many columns a table has, read off its data-column-widths attribute (one
        /// entry per column). Falls back to the number of cells, which is right for the
        /// single-row table that is all we could sensibly assume without the attribute.
        /// </summary>
        public static int ColumnCount(SafeXmlElement table)
        {
            var widths = table.GetAttribute(ColumnWidthsAttribute) ?? "";
            var count = widths.Split(',').Count(entry => !string.IsNullOrWhiteSpace(entry));
            if (count > 0)
                return count;
            return Math.Max(1, CellsOf(table).Count);
        }

        /// <summary>
        /// What kind of content a cell holds: its data-content-type if it has one,
        /// otherwise worked out from what is actually in it (a table built by hand, e.g.
        /// in a page template, may not carry the attribute). Text is the default, which is
        /// also the library's default content type.
        /// </summary>
        public static string ContentTypeOf(SafeXmlElement cell)
        {
            var declared = cell.GetAttribute(ContentTypeAttribute);
            if (!string.IsNullOrWhiteSpace(declared))
                return declared;
            if (FindNestedTable(cell) != null)
                return TableContentType;
            if (FindDescendantWithClass(cell, "bloom-videoContainer") != null)
                return VideoContentType;
            if (
                FindDescendantWithClass(cell, "bloom-canvas") != null
                || FindDescendantWithClass(cell, "bloom-imageContainer") != null
            )
                return ImageContentType;
            return TextContentType;
        }

        /// <summary>
        /// The table nested directly in a cell (the content of a cell whose type is
        /// "table"), or null.
        /// </summary>
        public static SafeXmlElement FindNestedTable(SafeXmlElement cell)
        {
            return SafeXmlElement
                .GetAllDivsWithClass(cell, TableClass)
                .FirstOrDefault(table => ParentCellOf(table) == cell);
        }

        /// <summary>
        /// The cell a nested table sits in, or null if the table is not in a cell.
        /// </summary>
        public static SafeXmlElement ParentCellOf(SafeXmlElement table)
        {
            for (var parent = table.ParentNode as SafeXmlElement; parent != null; )
            {
                if (parent.HasClass(CellClass))
                    return parent;
                if (IsTable(parent))
                    return null; // a table's own children are cells; we passed the boundary
                parent = parent.ParentNode as SafeXmlElement;
            }
            return null;
        }

        /// <summary>
        /// The first descendant of the cell with the given class that belongs to this cell
        /// rather than to a table nested inside it, or null.
        /// </summary>
        public static SafeXmlElement FindDescendantWithClass(SafeXmlElement cell, string className)
        {
            return SafeXmlElement
                .GetAllDivsWithClass(cell, className)
                .FirstOrDefault(element =>
                    !IsInsideTable(element) || ParentCellOf(element) == cell
                );
        }

        /// <summary>
        /// Every attribute of a table or cell element, as the JSON object that goes into
        /// the [details] cell, with the edit-time artifacts removed: the whole-attribute
        /// ones dropped, and the edit-only classes and style properties taken out of the
        /// class and style attributes. An attribute left empty by that trimming is dropped
        /// rather than written as "".
        /// </summary>
        public static JObject GetAttributesForExport(SafeXmlElement element)
        {
            var result = new JObject();
            foreach (var pair in element.AttributePairs ?? new NameValue[0])
            {
                if (kEditOnlyAttributes.Contains(pair.Name))
                    continue;
                var value = pair.Value ?? "";
                if (pair.Name == "class")
                    value = StripEditOnlyClasses(value);
                else if (pair.Name == "style")
                    value = StripEditOnlyStyleProperties(value);
                if (
                    string.IsNullOrWhiteSpace(value)
                    && (pair.Name == "class" || pair.Name == "style")
                )
                    continue;
                result[pair.Name] = value;
            }
            return result;
        }

        /// <summary>
        /// The class attribute with the edit-time classes taken out.
        /// </summary>
        public static string StripEditOnlyClasses(string classes)
        {
            return string.Join(
                " ",
                classes
                    .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(name =>
                        !kEditOnlyClasses.Contains(name)
                        && !name.StartsWith(kEditOnlyClassPrefix, StringComparison.Ordinal)
                    )
            );
        }

        /// <summary>
        /// The style attribute with the edit-time properties taken out. Declarations are
        /// otherwise left exactly as they were written, since the renderer's inline
        /// grid-template styles are what make an imported table lay out correctly.
        /// </summary>
        public static string StripEditOnlyStyleProperties(string style)
        {
            var kept = style
                .Split(';')
                .Where(declaration => !string.IsNullOrWhiteSpace(declaration))
                .Select(declaration => declaration.Trim())
                .Where(declaration =>
                {
                    var colon = declaration.IndexOf(':');
                    if (colon < 0)
                        return true; // not a declaration we understand; keep it
                    var property = declaration.Substring(0, colon).Trim();
                    return !kEditOnlyStyleProperties.Contains(property);
                })
                .ToList();
            if (kept.Count == 0)
                return "";
            return string.Join("; ", kept) + ";";
        }

        /// <summary>
        /// Puts the attributes from a [details] JSON object onto a freshly made table or
        /// cell element, verbatim. The inverse of GetAttributesForExport.
        /// </summary>
        public static void ApplyAttributes(SafeXmlElement element, JObject attributes)
        {
            if (attributes == null)
                return;
            foreach (var property in attributes.Properties())
            {
                // Guard only against a name the XML parser would refuse; we deliberately do
                // not filter or interpret the values.
                var name = property.Name;
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                element.SetAttribute(name, property.Value.ToString());
            }
        }
    }
}
