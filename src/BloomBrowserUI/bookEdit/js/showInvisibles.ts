import bloomQtipUtils from "./bloomQtipUtils";
import $ from "jquery";

// For testing and debugging, functionality to replace invisible characters with symbols.

const invisibles = [
    { name: "Zero Width Joiner", re: "&zwj;|\u200D", symbol: "\uE077" },
    { name: "Word Joiner", re: "&NoBreak;|\u2060", symbol: "\uE078" },
    {
        name: "Zero Width Non-Joiner",
        re: "&zwnj;|\u200C",
        symbol: "\uE079",
    },
    { name: "Combining Grapheme Joiner", re: "\u034F", symbol: "\uE07A" },
    {
        name: "Zero Width Space",
        re: "&ZeroWidthSpace;|&NegativeMediumSpace;|&NegativeThickSpace;|&NegativeThinSpace;|&NegativeVeryThinSpace;|\u200B",
        symbol: "\uE081",
    },
    {
        name: "No-Break Space",
        re: "&nbsp;|&NonBreakingSpace;|\u00A0",
        symbol: "\uE082",
    },
    { name: "Narrow No-Break Space", re: "\u202F", symbol: "\uE083" },
    { name: "En Quad", re: "\u2000", symbol: "\uE084" },
    { name: "Em Quad", re: "\u2001", symbol: "\uE085" },
    { name: "En Space", re: "&ensp;|\u2002", symbol: "\uE086" },
    { name: "Em Space", re: "&emsp;|\u2003", symbol: "\uE087" },
    { name: "Three-Per-Em Space", re: "&emsp13;|\u2004", symbol: "\uE088" },
    { name: "Four-Per-Em Space", re: "&emsp14;|\u2005", symbol: "\uE089" },
    { name: "Six-Per-Em Space", re: "\u2006", symbol: "\uE08A" },
    { name: "Figure Space", re: "&numsp;|\u2007", symbol: "\uE08B" },
    { name: "Punctuation Space", re: "&puncsp;|\u2008", symbol: "\uE08C" },
    {
        name: "Thin Space",
        re: "&thinsp;|&ThinSpace;|\u2009",
        symbol: "\uE08D",
    },
    {
        name: "Hair Space",
        re: "&VeryThinSpace;|&hairsp;|\u200A",
        symbol: "\uE08E",
    },
    { name: "Left-To-Right Mark", re: "&lrm;|\u200E", symbol: "\uE090" },
    { name: "Right-To-Left Mark", re: "&rlm;|\u200F", symbol: "\uE091" },
    { name: "Soft Hyphen", re: "&shy;|\u00AD", symbol: "\uE0A1" },
    { name: "Horizontal Tabulation", re: "&Tab;|\u0009", symbol: "\uE0A2" },
];

let inShowInvisiblesMode = false;

function invisibleToCode(invisible) {
    // check if it is an html entity and return it if so
    if (invisible[0] === "&" && invisible[invisible.length - 1] === ";") {
        return invisible;
    }
    if (invisible.length > 1) {
        throw new Error(
            "Something went wrong - invisible character is neither an html entity nor a single UTF-16 character: " +
                invisible,
        );
    }
    const codePoint = invisible.charCodeAt(0);
    let unicodePoint = codePoint.toString(16);
    while (unicodePoint.length < 4) {
        unicodePoint = "0" + unicodePoint;
    }
    return "\\u" + unicodePoint;
}

function invisibleFromCode(unicodeCode, entity) {
    if (unicodeCode && entity) {
        console.error(
            "Something went wrong - we got both a unicode code and an html entity: " +
                unicodeCode +
                " and " +
                entity,
        );
        return "";
    }
    if (unicodeCode) {
        return String.fromCharCode(parseInt(unicodeCode, 16));
    }
    return entity;
}

export function showInvisibles(e) {
    e.preventDefault();

    // get the parent that has the class bloom-editable
    const editable = $(e.target).closest(".bloom-editable");
    if (inShowInvisiblesMode) {
        return;
    }
    inShowInvisiblesMode = true;
    editable.html((i, html) => {
        // for each replacement, replace all instances of the invisible char/entity with the symbol
        invisibles.forEach(function (invisibleType) {
            // Just in case there were invisibles-replacement spans left in the document,
            // don't match anything immediately following data-original="
            const re = new RegExp(
                `(?<!data-original=\")(${invisibleType.re})`,
                "g",
            );
            html = html.replace(re, (match) => {
                const code = invisibleToCode(match); // get something like \u00A0 for unicode chars. leaves html entities as is.
                return `<span class="invisibles" data-name-of-invisible="${invisibleType.name}" data-original="${code}">${invisibleType.symbol}</span>`;
            });
            return html;
        });
        return html;
    });
    // Make one qtip per type of invisible character to explain the symbol to the user without cluttering the page too much
    const invisibleCharTypesWithQtips = new Set();

    $(".invisibles").each(function () {
        const $this: JQuery = $(this);
        if (
            !invisibleCharTypesWithQtips.has(
                $this.attr("data-name-of-invisible"),
            )
        ) {
            invisibleCharTypesWithQtips.add(
                $this.attr("data-name-of-invisible"),
            );
            $this.qtip({
                position: {
                    my: "top left",
                    at: "bottom right",
                    container: bloomQtipUtils.qtipZoomContainer(),
                },
                content: $this.attr("data-name-of-invisible"),
                show: {
                    ready: true,
                },
                hide: {
                    inactive: 2000,
                    effect: function () {
                        $(this).fadeOut(250);
                    },
                },
            });
        }
    });
}

export function hideInvisibles(e) {
    if (inShowInvisiblesMode) {
        inShowInvisiblesMode = false;

        // destroy all qtips
        $(".invisibles").each(function () {
            const $this: JQuery = $(this);
            $this.qtip("destroy");
        });

        // restore all the original characters
        const editable = $(e.target).closest(".bloom-editable");
        editable.html((i, html) => {
            return html.replace(
                /<span class="invisibles"[^<>]*data-original="(?:\\u(....)|(&[a-z,0-9]*;))"[^<>]*>.<\/span>/g,
                // p1 will match unicode char codes, p2 will html entities
                function (match, p1, p2) {
                    return invisibleFromCode(p1, p2);
                },
            );
        });
    }
}
