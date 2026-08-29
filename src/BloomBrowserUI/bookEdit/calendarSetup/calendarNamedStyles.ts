// The named styles a calendar month grid's text uses, and how to give them to a book that has
// never held a calendar.
//
// A book made from the Wall Calendar template defines these in the template's own style sheet.
// A grid the user drops onto a canvas in any other book has nothing defining them, so its
// weekday names and its dates would come out at the size of ordinary text. So the factory that
// makes such a grid puts the definitions into the page's userModifiedStyles sheet, which is
// where Bloom keeps the styles a book carries with it: Bloom's save machinery merges that sheet
// into the book, and the Format dialog edits the same rules afterwards.
//
// KEEP IN STEP: these are the defaults written in
// src/content/templates/template books/Wall Calendar/Wall Calendar.pug.

/** One named style and the declarations a new book should start it with. */
interface ICalendarNamedStyle {
    /** The style's name, as it appears in a class such as "CalendarDayNumber-style". */
    name: string;
    /** The declarations of the rule, without the braces. */
    declarations: string;
}

// The month name is a text field of the page, outside the grid, so its style
// (CalendarMonth-style) is not this component's to define. The year is not a text field at
// all: it is an invisible data-book element the tooling reads, so it carries no style.
const kCalendarNamedStyles: ICalendarNamedStyle[] = [
    {
        name: "CalendarDayOfWeek",
        declarations:
            "font-size: 12pt !important; font-weight: bold !important; text-align: center !important;",
    },
    {
        name: "CalendarDayNumber",
        declarations:
            "font-size: 14pt !important; font-style: italic !important;",
    },
    { name: "CalendarDayNote", declarations: "font-size: 9pt !important;" },
];

/**
 * The document's userModifiedStyles sheet, making it if the document has none. The same
 * mechanism as StyleEditor.GetOrCreateUserModifiedStyleSheet: the sheet is identified by its
 * title, and a newly-created style element only becomes a style sheet once the browser has
 * taken it up, so we look for it again rather than using what we just made.
 */
function getOrCreateUserModifiedStyleSheet(
    documentToUse: Document,
): CSSStyleSheet | null {
    const find = (): CSSStyleSheet | null => {
        for (let i = 0; i < documentToUse.styleSheets.length; i++) {
            const ownerNode = documentToUse.styleSheets[i]
                .ownerNode as HTMLElement | null;
            if (ownerNode?.title === "userModifiedStyles") {
                return documentToUse.styleSheets[i] as CSSStyleSheet;
            }
        }
        return null;
    };
    const existing = find();
    if (existing) return existing;
    const newSheet = documentToUse.createElement("style");
    newSheet.title = "userModifiedStyles";
    newSheet.type = "text/css";
    documentToUse.getElementsByTagName("head")[0].appendChild(newSheet);
    return find();
}

/** Whether the sheet already has a rule for the style of this name. */
function styleIsDefined(sheet: CSSStyleSheet, name: string): boolean {
    const selector = `.${name}-style`;
    for (let i = 0; i < sheet.cssRules.length; i++) {
        const rule = sheet.cssRules[i] as CSSStyleRule;
        if (rule.selectorText?.trim().startsWith(selector)) return true;
    }
    return false;
}

/**
 * Make sure the document defines each of the calendar's named styles, adding the ones it does
 * not have. A style the book already defines is left alone, whatever it says, because it is
 * either the template's own or something the user has since chosen in the Format dialog.
 */
export function ensureCalendarStylesAreDefined(documentToUse: Document): void {
    const sheet = getOrCreateUserModifiedStyleSheet(documentToUse);
    if (!sheet) return;
    kCalendarNamedStyles.forEach((style) => {
        if (styleIsDefined(sheet, style.name)) return;
        sheet.insertRule(
            `.${style.name}-style { ${style.declarations} }`,
            sheet.cssRules.length,
        );
    });
}
