/// <reference path="../../typings/bundledFromTSC.d.ts"/>
/// <reference path="../../typings/jquery/jquery.d.ts" />
/// <reference path="../../typings/select2/select2.d.ts" />
/// <reference path="../../lib/localizationManager/localizationManager.ts" />
/// <reference path="../../lib/jquery.i18n.custom.ts" />
/// <reference path="../../lib/misc-types.d.ts" />
/// <reference path="../../lib/jquery.alphanum.d.ts"/>
/// <reference path="../js/toolbar/toolbar.d.ts"/>
// This collectionSettings reference defines the function GetSettings(): ICollectionSettings
// The actual function is injected by C#.
/// <reference path="../js/collectionSettings.d.ts"/>
/// <reference path="../OverflowChecker/OverflowChecker.ts"/>

import "../../node_modules/select2/dist/js/select2.js";

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import OverflowChecker from "../OverflowChecker/OverflowChecker";
import {
    IsPageXMatter,
    SetupThingsSensitiveToStyleChanges
} from "../js/bloomEditing";
import "../../lib/jquery.alphanum";
import axios from "axios";
import { get, wrapAxios } from "../../utils/bloomApi";
import { EditableDivUtils } from "../js/editableDivUtils";
import * as ReactDOM from "react-dom";
import FontSelectComponent, { IFontMetaData } from "./fontSelectComponent";
import React = require("react");
import {
    ISimpleColorPickerDialogProps,
    showSimpleColorPickerDialog
} from "../../react_components/color-picking/colorPickerDialog";
import { BloomPalette } from "../../react_components/color-picking/bloomPalette";
import { kBloomYellow } from "../../bloomMaterialUITheme";
import { RenderRoot } from "./AudioHilitePage";
import { RenderOverlayRoot } from "./OverlayFormatPage";
import { BubbleManager } from "../js/bubbleManager";

// Controls the CSS text-align value
// Note: CSS text-align W3 standard does not specify "start" or "end", but Firefox/Chrome/Edge do support it.
// Note: CSS text-align also has values "initial", and "inherit", which we do not support currently.
type Alignment = "start" | "center" | "justify" | "end";

interface IFormattingValues {
    ptSize: string;
    fontName: string;
    lineHeight: string;
    wordSpacing: string;
    alignment: Alignment;
    paraSpacing: string;
    paraIndent: string;
    bold: boolean;
    italic: boolean;
    underline: boolean;
    color: string;
    // I'm allowing undefined here partly to emphasize that it's very possible to
    // leave hiliteTextColor unspecified. In contrast, if we don't want a
    // background color, we need to make it explicitly transparent, since there
    // is a default rule to override. In practice, though, the 'undefined' value
    // is rarely if ever used, since if there is a hilite style rule at all,
    // its 'color' property is an empty string rather than undefined or null
    // when it is not specified.
    hiliteTextColor: string | undefined;
    hiliteBgColor: string;
    padding: string;
}

// Class provides a convenient way to group a style id and display name
class FormattingStyle {
    public styleId: string;
    public englishDisplayName: string;
    public localizedName: string;

    constructor(namestr: string, displayStr: string) {
        this.styleId = namestr;
        this.englishDisplayName = displayStr;
    }

    public hasStyleId(name: string): boolean {
        return this.styleId.toLowerCase() === name.toLowerCase();
    }

    public getLocalizedName(): string {
        // null-coalesce operator would be handy here.
        return this.localizedName || this.englishDisplayName;
    }
}

export default class StyleEditor {
    private _previousBox: Element;
    _observer: ResizeObserver;
    private _supportFilesRoot: string;
    private MIN_FONT_SIZE: number = 7;
    public boxBeingEdited: HTMLElement; // public for testing
    private ignoreControlChanges: boolean;
    private styles: FormattingStyle[];
    private xmatterMode: boolean; // true if we are in xmatter (and shouldn't change fixed style names)
    private textColorTitle: string = "Text Color";

    constructor(supportFilesRoot: string) {
        this._supportFilesRoot = supportFilesRoot;
    }

    public static GetStyleClassFromElement(target: HTMLElement): string | null {
        let c = $(target).attr("class");
        if (!c) {
            c = "";
        }
        const classes = c.split(" ");

        for (let i = 0; i < classes.length; i++) {
            if (classes[i].indexOf("-style") > 0) {
                return classes[i];
            }
        }

        // For awhile between v1 and v2 we used 'coverTitle' in Factory-XMatter
        // In case this is one of those books, we'll replace it with 'Title-On-Cover-style'
        let coverTitleClass: string | null = StyleEditor.updateCoverStyleName(
            target,
            "coverTitle"
        );

        // For awhile in v2 we used 'coverTitle-style' in Factory-XMatter
        // In case this is one of those books, we'll replace it with 'Title-On-Cover-style'
        if (!coverTitleClass) {
            coverTitleClass = StyleEditor.updateCoverStyleName(
                target,
                "coverTitle-style"
            );
        }

        return coverTitleClass;
    }

    private static updateCoverStyleName(
        target: HTMLElement,
        oldCoverTitleClass: string
    ): string | null {
        if ($(target).hasClass(oldCoverTitleClass)) {
            const newStyleName: string = "Title-On-Cover-style";
            $(target)
                .removeClass(oldCoverTitleClass)
                .addClass(newStyleName);
            return newStyleName;
        }

        return null;
    }

    private static MigratePreStyleBook(target: HTMLElement): string | null {
        const parentPage: HTMLDivElement = <HTMLDivElement>(
            (<any>$(target).closest(".bloom-page")[0])
        );
        // Books created with the original (0.9) version of "Basic Book", lacked "x-style"
        // but had all pages starting with an id of 5dcd48df (so we can detect them)
        const pageLineage = $(parentPage).attr("data-pagelineage");
        if (pageLineage && pageLineage.substring(0, 8) === "5dcd48df") {
            const styleName: string = "normal-style";
            $(target).addClass(styleName);
            return styleName;
        }
        return null;
    }

    private static GetStyleNameForElement(target: HTMLElement): string | null {
        let styleName: string | null = this.GetStyleClassFromElement(target);
        if (!styleName) {
            // The style name is probably on the parent translationGroup element
            const parentGroup: HTMLDivElement = <HTMLDivElement>(
                (<any>$(target).parent(".bloom-translationGroup")[0])
            );
            if (parentGroup) {
                styleName = this.GetStyleClassFromElement(parentGroup);
                if (styleName) {
                    $(target).addClass(styleName); // add style to bloom-editable div
                } else {
                    return this.MigratePreStyleBook(target);
                }
            } else {
                // No .bloom-translationGroup? Unlikely...
                return this.MigratePreStyleBook(target);
            }
        }
        // For awhile between v1 and v2 we used 'default-style' in Basic Book
        // In case this is one of those books, we'll replace it with 'normal-style'
        if (styleName === "default-style") {
            $(target).removeClass(styleName);
            styleName = "normal-style"; // This will be capitalized before presenting it to the user.
            $(target).addClass(styleName);
        }
        return styleName;
    }

    private static GetBaseStyleNameForElement(
        target: HTMLElement
    ): string | null {
        const styleName = StyleEditor.GetStyleNameForElement(target); // with '-style'
        if (styleName == null) return null;
        const suffixIndex = styleName.indexOf("-style");
        if (suffixIndex < 0) {
            return styleName;
        }
        return styleName.substr(0, suffixIndex);
    }

    // Make the specified style (which should include the trailing '-style') the style for the target element,
    // and if it's in a translation group, for all the siblings that are bloom-editable.
    private static SetStyleNameForElement(
        target: HTMLElement,
        newStyle: string
    ) {
        this.SetStyleNameForLeaf(target, newStyle);
        // If this is a translation group, we need to set the style on all the siblings
        // that are bloom-editable
        const group = target.closest(".bloom-translationGroup");
        if (!group) return;
        // This will typically include the target itself, but it's not very costly to do
        // it twice to that one element.
        const siblings = Array.from(
            group.getElementsByClassName("bloom-editable")
        );
        siblings.forEach(sibling =>
            this.SetStyleNameForLeaf(sibling as HTMLElement, newStyle)
        );
    }

    private static SetStyleNameForLeaf(target: HTMLElement, newStyle: string) {
        const oldStyle: string | null = this.GetStyleClassFromElement(target);
        if (oldStyle != null) $(target).removeClass(oldStyle);
        $(target).addClass(newStyle);
    }

    private static GetLangValueOrNull(target: HTMLElement): string | null {
        const langAttr = $(target).attr("lang");
        if (!langAttr) {
            return null;
        }
        return langAttr.valueOf().toString();
    }

    public GetCalculatedFontSizeInPoints(target: HTMLElement): number {
        const sizeInPx = $(target).css("font-size");
        return this.ConvertPxToPt(parseInt(sizeInPx, 10));
    }

    // Get the names that should be offered in the styles combo box.
    // Basically any defined rules for classes that end in -style.
    // Only the last class in a sequence is used; this lets us predefine
    // styles like DIV.bloom-editing.Heading1 and make their selectors specific enough to work,
    // but not impossible to override with a custom definition.
    public getFormattingStyles(): FormattingStyle[] {
        const styles: FormattingStyle[] = [];
        for (let i = 0; i < document.styleSheets.length; i++) {
            const sheet = document.styleSheets[i] as CSSStyleSheet;
            let rules: CSSRuleList | null = null;
            try {
                rules = sheet?.cssRules;
            } catch {
                // We had a problem with a style sheet that had inaccessible rules.
                // This will allow the StyleEditor to continue to work.
                continue;
            }
            if (rules) {
                for (let j = 0; j < rules.length; j++) {
                    const index = rules[j].cssText.indexOf("{");
                    if (index === -1) {
                        continue;
                    }
                    const label = rules[j].cssText.substring(0, index).trim();
                    const index2 = label.lastIndexOf("-style");
                    if (
                        index2 !== -1 &&
                        index2 === label.length - "-style".length
                    ) {
                        // ends in -style
                        const index3 = label.lastIndexOf(".");
                        const styleId = label.substring(index3 + 1, index2);
                        // Get the English display name if one is defined for this style
                        const displayName = this.getDisplayName(styleId);
                        if (styles.every(style => !style.hasStyleId(styleId))) {
                            styles.push(
                                new FormattingStyle(styleId, displayName)
                            );
                        }
                    }
                }
            }
        }
        // 'normal' is the standard initial style for at least origami pages.
        // But our default template doesn't define it; by default it just has default properties.
        // Make sure it's available to choose again.
        if (styles.every(style => !style.hasStyleId("normal"))) {
            styles.push(new FormattingStyle("normal", "Normal"));
        }
        return styles;
    }

    // Gets the English Display name for the default styles that are used in Bloom code
    // We were thinking of using a custom css property in the style css definition,
    // but we would have needed this switch to deal with existing books anyway, so...
    // we'll just use the switch.
    // Changes here should be reflected in BookSettingsApi.cs: BookSettingsApi.GetEnglishStyleName().
    // Changes here should be reflected in the Bloom.xlf file too.
    public getDisplayName(ruleId: string): string {
        switch (ruleId) {
            case "BigWords":
                return "Big Words";
            case "Cover-Default":
                return "Cover Default";
            case "Credits-Page":
                return "Credits Page";
            case "Heading1":
                return "Heading 1";
            case "Heading2":
                return "Heading 2";
            case "normal":
                return "Normal";
            case "Title-On-Cover":
                return "Title On Cover";
            case "Title-On-Title-Page":
                return "Title On Title Page";
            case "ImageDescriptionEdit":
                return "Image Description Edit";
            case "QuizHeader":
                return "Quiz Header";
            case "QuizQuestion":
                return "Quiz Question";
            case "QuizAnswer":
                return "Quiz Answer";
            case "Equation": // If the id is the same as the English, just fall through to default.
            default:
                return ruleId;
        }
    }

    // Get the predefined rule for a style that is not yet used in this document.
    // It was probably a case of premature optimization, but we decided at some point not to
    // have all the predefined styles embedded in every document, or even defined in a stylesheet
    // that is included in every document in all circumstances. Instead, some common ones are
    // defined in editMode.less, while some are defined in particular xmatter packs, and maybe
    // even elsewhere. This code searches all the stylesheets that the document loads and picks
    // the one that will currently win (that is, the last found) if there is more than one.
    // It then modifies the rule, for example, making everything !important as we do for
    // the settings the user makes, and possibly breaking font family out into a separate,
    // language-specific rule.
    // Will return null if the style has no definition, OR if it already has a user-defined version in this document
    public getMissingPredefinedStyle(target: string): CSSRule | null {
        let result: CSSRule | null = null;

        // Get the book's non-inlined stylesheets (notably, excluding the stylesheets for the Bloom UI)
        // along with the book's userModifiedStyles (which is inlined)
        // Note: we exclude some of the book HTM's inlined stylesheets, but they're kinda complicated to identify and not needed, so we ignore them
        const styleSheets = this.getBookNonInlineStyleSheets();
        const userModifiedStyles = this.FindExistingUserModifiedStyleSheet();
        if (userModifiedStyles) {
            styleSheets.push(userModifiedStyles);
        }

        for (let i = 0; i < styleSheets.length; i++) {
            const sheet = styleSheets[i];
            const rules = sheet.cssRules;
            if (rules) {
                for (let j = 0; j < rules.length; j++) {
                    const index = rules[j].cssText.indexOf("{");
                    if (index === -1) {
                        continue;
                    }
                    const label = rules[j].cssText.substring(0, index).trim();
                    // Partial match is here to support comma-separated rules (multiple selector syntax)
                    if (label.indexOf(target) >= 0) {
                        // We have a rule for our target!
                        // Is this the user-defined stylesheet?
                        if (sheet === userModifiedStyles) {
                            return null; // style already has a user definition
                        } else {
                            // return the last one we find.
                            // This is not strictly sound, there COULD be many rules for this style which each
                            // contribute different properties. Choosing not to handle this case.
                            result = rules[j];
                        }
                    }
                }
            }
        }
        return result;
    }

    /**
     * Returns the book's stylesheets which are non-inline a.k.a. external (external from the HTML document's perspective, not from the book's perspective)
     * Notably, by "book", we mean it excludes any stylesheets that are for the Bloom program's UI, rather than for the book.
     * By "non-inline", we mean that it excludes the stylesheets in the book's .htm file
     */
    private getBookNonInlineStyleSheets(): CSSStyleSheet[] {
        const styleSheets: CSSStyleSheet[] = [];
        for (let i = 0; i < document.styleSheets.length; ++i) {
            styleSheets.push(document.styleSheets[i]);
        }

        // We expect the document path to look like this:
        // http://localhost:8089/bloom/[pathToBook]/currentPage-memsim-Normal.html'
        const lowercaseHref = document.location.href.toLowerCase();
        const expectedLowercaseEnding = "/currentpage-memsim-normal.html"; // Note: the actual page name has a couple capital letters; this is the lowercased version
        const isExpectedForm = lowercaseHref.endsWith(expectedLowercaseEnding);
        console.assert(
            isExpectedForm,
            `document.location.href expected to end with "currentPage-memsim-Normal.html", but actually was "${document.location.href}"`
        );

        if (!isExpectedForm) {
            // Just return empty array, nothing is going to match anyway
            return [];
        }

        const endingStartIndex = lowercaseHref.indexOf(expectedLowercaseEnding);

        // Our searching / validation was case-insensitive (by converting to lowercase),
        // but now we want to resume with the properly-cased version.
        const urlToBookDirectory = document.location.href.substring(
            0,
            endingStartIndex
        );

        // Note: Several style elements can have href=null. This indicates inline style elements that are directly added to head.
        // (This includes both inline style elements directly from the book's HTM file, as well as other style elements added to head later)
        const filteredArray = styleSheets.filter(
            sheet =>
                sheet.href != null && sheet.href.startsWith(urlToBookDirectory)
        );
        console.assert(
            filteredArray.length > 0,
            "Error? Array length is 0! (No stylesheets left after calling getBookStyleSheets). Please investigate if this is expected"
        );
        return filteredArray;
    }

    private FindExistingUserModifiedStyleSheet(): CSSStyleSheet | null {
        for (let i = 0; i < document.styleSheets.length; i++) {
            if (
                (<HTMLElement>document.styleSheets[i].ownerNode).title ===
                "userModifiedStyles"
            ) {
                // alert("Found userModifiedStyles sheet: i= " + i + ", title= " +
                //  (<StyleSheet>(<any>document.styleSheets[i]).ownerNode).title + ", sheet= " +
                //  document.styleSheets[i].ownerNode.textContent);
                return <CSSStyleSheet>document.styleSheets[i];
            }
        }
        return null;
    }

    //note, this currently just makes an element in the document, not a separate file
    public GetOrCreateUserModifiedStyleSheet(): CSSStyleSheet | null {
        let styleSheet = this.FindExistingUserModifiedStyleSheet();
        if (styleSheet == null) {
            const newSheet = document.createElement("style");
            document.getElementsByTagName("head")[0].appendChild(newSheet);
            newSheet.title = "userModifiedStyles";
            newSheet.type = "text/css";

            //at this point, we are tempted to just return newSheet, but in the FF29 we're using,
            //that is just an element at this point, not a really stylesheet.
            //so we just go searching for it again in the document.styleSheets array, and this time we will find it.
            styleSheet = this.FindExistingUserModifiedStyleSheet();
        }
        return styleSheet;
    }

    // Get a style rule with a specified name that can be modified to change the appearance of text in this style.
    // If ignoreLanguage is true, this will be a rule that just specifies the name (.myStyle). This is
    // always used for the More tab, and for everything except font name when authoring.
    // Otherwise, it will specify language: .myStyle[lang="code"], or if langAttrValue is null, .myStyle:not([lang]).
    // This is used for all of character tab when localizing, and always for font name.
    // if forChildParas is true, the rule sought will have a selector like ".mystyle-style p" to select paragraphs
    // inside the block that has the style.
    public GetOrCreateRuleForStyle(
        styleName: string,
        langAttrValue: string | null,
        ignoreLanguage: boolean,
        forChildParas?: boolean
    ): CSSStyleRule | null {
        let addToSelector = "";
        // if we are authoring a book, style changes should apply to all translations of it
        // if we are translating, changes should only apply to this language.
        // a downside of this is that when authoring in multiple languages, to get a different
        // appearance for different languages a different style must be created.
        if (!ignoreLanguage) {
            if (langAttrValue && langAttrValue.length > 0) {
                addToSelector += '[lang="' + langAttrValue + '"]';
            } else {
                addToSelector += ":not([lang])";
            }
        }

        if (forChildParas) {
            addToSelector += " > p";
        }

        return this.GetRuleForStyle(styleName, addToSelector, true);
    }

    public GetRuleForStyle(
        styleName: string, // The main name, such as "Heading1"
        // modifiers identifying which particular rule for that style we want,
        // such as [lang='fr'] or span.ui-audioCurrent
        addToSelector: string,
        // If there is not already such a rule, should we create it, or return null?
        create: boolean
    ): CSSStyleRule | null {
        const styleSheet = this.GetOrCreateUserModifiedStyleSheet();
        if (styleSheet == null) {
            return null;
        }

        let ruleList: CSSRuleList = styleSheet.cssRules;
        if (ruleList == null) {
            ruleList = new CSSRuleList();
        }

        let selector = styleName + addToSelector;
        const lookFor = selector.toLowerCase();

        for (let i = 0; i < ruleList.length; i++) {
            const index = ruleList[i].cssText.indexOf("{");
            if (index === -1) {
                continue;
            }
            // The rule we want is one whose selector is the string we want.
            // The substring strips off the initial period and the rule body, leaving the selector.
            const match = ruleList[i].cssText
                .trim()
                .substring(1, index)
                .toLowerCase()
                .trim();
            if (match === lookFor) {
                return <CSSStyleRule>ruleList[i];
            }
        }
        if (!create) {
            return null;
        }
        selector = "." + selector;
        styleSheet.insertRule(selector + " { }", ruleList.length);

        return <CSSStyleRule>ruleList[ruleList.length - 1]; //new guy is last
    }

    // What we stick after something like ".normal-style" to make a selector that targets
    // the current audio element (or its paragraph children).
    private sentenceHiliteRuleSelector = " span.ui-audioCurrent";
    // Spans with padding get split into multiple embedded spans, so we need to target the
    // inner span.
    private paddedSentenceHiliteRuleSelector =
        " span.ui-audioCurrent > span.ui-enableHighlight";
    // note, no leading space here. We want (for example) .normal-style.ui-audioCurrent since this rule
    // targets textbox mode where both classes occur on the same element.
    private paraHiliteRuleSelector = ".ui-audioCurrent p";

    // Update the DOM with the rules that will make the current audio element have
    // the specified properties if it occurs in a block with the specified style.
    public putAudioHiliteRulesInDom(
        styleName: string,
        hiliteTextColor: string | undefined,
        hiliteBgColor: string
    ) {
        const sentenceRule = this.GetRuleForStyle(
            styleName,
            this.sentenceHiliteRuleSelector,
            true
        );
        this.updateHiliteStyleRuleBody(
            sentenceRule,
            hiliteTextColor,
            hiliteBgColor
        );
        const paddedSentenceRule = this.GetRuleForStyle(
            styleName,
            this.paddedSentenceHiliteRuleSelector,
            true
        );
        this.updateHiliteStyleRuleBody(
            paddedSentenceRule,
            hiliteTextColor,
            hiliteBgColor
        );

        const paraRule = this.GetRuleForStyle(
            styleName,
            this.paraHiliteRuleSelector,
            true
        );
        this.updateHiliteStyleRuleBody(
            paraRule,
            hiliteTextColor,
            hiliteBgColor
        );
        this.cleanupAfterStyleChange();
    }

    private updateHiliteStyleRuleBody(
        rule: CSSStyleRule | null,
        hiliteTextColor: string | undefined,
        hiliteBgColor: string
    ) {
        if (!rule) {
            return; // paranoia
        }
        rule.style.setProperty("background-color", hiliteBgColor);
        if (hiliteTextColor) {
            rule.style.setProperty("color", hiliteTextColor);
        } else {
            rule.style.removeProperty("color");
        }
    }

    public getAudioHiliteProps(
        styleName: string
    ): { hiliteTextColor: string | undefined; hiliteBgColor: string } {
        const sentenceRule = this.GetRuleForStyle(
            styleName,
            // The two should have the same content, so for reading, we only need one.
            this.sentenceHiliteRuleSelector,
            false
        );
        const hiliteTextColor = sentenceRule?.style?.color;
        let hiliteBgColor = sentenceRule?.style?.backgroundColor;
        if (!hiliteBgColor) {
            hiliteBgColor = kBloomYellow;
        }
        return {
            hiliteTextColor,
            hiliteBgColor
        };
    }

    // Replaces a style in 'sheet' at the specified 'index' with a (presumably) modified style.
    public ReplaceExistingStyle(
        sheet: CSSStyleSheet,
        index: number,
        newStyle: string
    ): void {
        sheet.deleteRule(index);
        sheet.insertRule(newStyle, index);
    }

    public ConvertPxToPt(pxSize: number, round = true): number {
        const tempDiv = document.createElement("div");
        tempDiv.style.width = "1000pt";
        document.body.appendChild(tempDiv);
        const ratio = 1000 / tempDiv.clientWidth;
        document.body.removeChild(tempDiv);
        if (round) {
            return Math.round(pxSize * ratio);
        } else {
            return pxSize * ratio;
        }
    }

    /**
     * Get the style information off of the target element to display in the tooltip
     * @param {HTMLElement} targetBox the element with the style information
     * @param {string} styleName the style whose information we are reporting
     * @return returns the tooltip string
     */
    public GetToolTip(targetBox: HTMLElement, styleName: string): string {
        //Review: Gordon (JH) I'm not clear if this is still used or why, since it seems to be duplicated in AttachToBox
        styleName = styleName.substr(0, styleName.length - 6); // strip off '-style'
        styleName = styleName.replace(/-/g, " "); //show users a space instead of dashes
        const box = $(targetBox);
        const sizeString = box.css("font-size"); // always returns computed size in pixels
        const pxSize = parseInt(sizeString, 10); // strip off units and parse
        const ptSize = this.ConvertPxToPt(pxSize);
        const lang = box.attr("lang");

        // localize
        const tipText =
            "Changes the text size for all boxes carrying the style '{0}' and language '{1}'.\nCurrent size is {2}pt.";
        return theOneLocalizationManager.getText(
            "EditTab.FormatDialog.FontSizeTip",
            tipText,
            styleName,
            lang,
            ptSize
        );
    }

    /**
     * Adds a tooltip to an element
     * @param element a JQuery object to add the tooltip to
     * @param toolTip the text of the tooltip to display
     * @param delay how many milliseconds we want to display the tooltip (defaults to 3sec) -- currently ignored
     */
    public AddQtipToElement(
        element: JQuery,
        toolTip: string,
        delay: number = 3000
    ) {
        if (element.length === 0) return;
        // When the element is a span or similar this produces the tooltip
        element.attr("title", toolTip);

        // if the element is a select being shadowed by a select2, we have to put the tooltip on
        // the critical element inside the select2.
        // (https://jsfiddle.net/8odneso7/2/ shows an alternative and nicer technique, but
        // I can't get it to work, and surmise that we're using an older version of select2
        // that doesn't have it. This version is probably very implementation-dependent
        // and may need rework if we ever update select2.)
        const select2target = element
            .next()
            .find("span.select2-selection__rendered");
        if (select2target.length) {
            select2target.attr("title", toolTip);
            // And unfortunately select2 updates the tooltip every time it changes, so we have
            // to arrange to reinstate it
            element.change(x => select2target.attr("title", toolTip));
            return;
        }

        // And then element might be a container with a select2 INSIDE it...
        this.AddQtipToElement(element.find("select"), toolTip, delay);
    }

    public static GetClosestValueInList(
        listOfOptions: Array<string>,
        valueToMatch: number
    ) {
        let lineHeight;
        for (let i = 0; i < listOfOptions.length; i++) {
            const optionNumber = parseFloat(listOfOptions[i]);
            if (valueToMatch === optionNumber) {
                lineHeight = listOfOptions[i];
                break;
            }
            if (valueToMatch <= optionNumber) {
                lineHeight = listOfOptions[i];
                // possibly it is closer to the option before
                if (i > 0) {
                    const prevOptionNumber = parseFloat(listOfOptions[i - 1]);
                    const deltaCurrent = optionNumber - valueToMatch;
                    const deltaPrevious = valueToMatch - prevOptionNumber;
                    if (deltaPrevious < deltaCurrent) {
                        lineHeight = listOfOptions[i - 1];
                    }
                }
                break;
            }
        }
        if (
            valueToMatch > parseFloat(listOfOptions[listOfOptions.length - 1])
        ) {
            lineHeight = listOfOptions[listOfOptions.length - 1];
        }
        return lineHeight;
    }

    public getPointSizes() {
        // perhaps temporary until we allow arbitrary values (BL-948), as a favor to Mike:
        return [
            "7",
            "8",
            "9",
            "10",
            "11",
            "12",
            "13",
            "14",
            "16",
            "18",
            "20",
            "22",
            "24",
            "26",
            "28",
            "30",
            "35",
            "40",
            "45",
            "50",
            "55",
            "60",
            "65",
            "70",
            "80",
            "90",
            "100"
        ];

        // Same options as Word 2010, plus 13 since used in heading2
        //return ['7', '8', '9', '10', '11', '12', '13', '14', '16', '18', '20', '22', '24', '26', '28', '36', '48', '72'];
    }

    public getLineSpaceOptions() {
        return [
            "0.7",
            "0.8",
            "1.0",
            "1.1",
            "1.2",
            "1.3",
            "1.4",
            "1.5",
            "1.6",
            "1.8",
            "2.0",
            "2.5",
            "3.0"
        ];
    }

    public getWordSpaceOptions(): string[] {
        return [
            theOneLocalizationManager.getText(
                "EditTab.FormatDialog.WordSpacingNormal",
                "Normal"
            ),
            theOneLocalizationManager.getText(
                "EditTab.FormatDialog.WordSpacingWide",
                "Wide"
            ),
            theOneLocalizationManager.getText(
                "EditTab.FormatDialog.WordSpacingExtraWide",
                "Extra Wide"
            )
        ];
    }

    // We need to get localized versions of all the default styles and not return until we get them all.
    // "all" function from http://hermanradtke.com/2011/05/12/managing-multiple-jquery-promises.html
    public all(promises: JQueryPromise<string>[]): JQueryPromise<any> {
        const deferred = $.Deferred();
        let fulfilled = 0;
        const length = promises.length;
        const results: string[] = new Array(length);

        if (length === 0) {
            deferred.resolve(results);
        } else {
            promises.forEach((promise: JQueryPromise<string>, i) => {
                promise.then(value => {
                    results[i] = value ? value : "";
                    fulfilled++;
                    if (fulfilled === length) {
                        deferred.resolve(results);
                    }
                });
            });
        }
        return deferred.promise();
    }

    // Collects all the style name promises to use in an async version of populateSelect()
    public getStylePromises(
        styles: FormattingStyle[]
    ): JQueryPromise<string>[] {
        const promises: JQueryPromise<string>[] = [];

        styles.forEach(formattingStyle => {
            const completeStyleName =
                "EditTab.FormatDialog.DefaultStyles." +
                formattingStyle.styleId +
                "-style";
            promises.push(
                theOneLocalizationManager.asyncGetText(
                    completeStyleName,
                    formattingStyle.englishDisplayName,
                    ""
                ) as JQueryPromise<string>
            );
        });
        return promises;
    }

    public getParagraphSpaceOptions() {
        return ["0", "0.5", "0.75", "1", "1.25"];
    }

    private getFontNameFromTextBox(textBox: JQuery): string {
        let fontName = textBox.css("font-family");
        if (fontName[0] === "'" || fontName[0] === '"') {
            fontName = fontName.substring(1, fontName.length - 1); // strip off quotes
        }
        return fontName;
    }

    // Returns an object giving the current selection for each format control.
    public getFormatValues(): IFormattingValues {
        const box = $(this.boxBeingEdited);
        const sizeString = box.css("font-size");
        const pxSize = parseInt(sizeString, 10);
        let ptSize = this.ConvertPxToPt(pxSize, false);
        ptSize = Math.round(ptSize);

        const lineHeightString = box.css("line-height");
        const lineHeightPx = parseInt(lineHeightString, 10);
        const lineHeightNumber =
            Math.round((lineHeightPx / pxSize) * 10) / 10.0;
        const lineSpaceOptions = this.getLineSpaceOptions();
        const lineHeight = StyleEditor.GetClosestValueInList(
            lineSpaceOptions,
            lineHeightNumber
        );

        const wordSpaceOptions = this.getWordSpaceOptions();

        const wordSpaceString = box.css("word-spacing");
        let wordSpacing = wordSpaceOptions[0];
        if (wordSpaceString !== "0px") {
            const pxSpace = parseInt(wordSpaceString, 10);
            const ptSpace = this.ConvertPxToPt(pxSpace);
            if (ptSpace > 7.5) {
                wordSpacing = wordSpaceOptions[2];
            } else {
                wordSpacing = wordSpaceOptions[1];
            }
        }
        const weight = box.css("font-weight");
        const bold = parseInt(weight, 10) > 600;

        const italic = box.css("font-style") === "italic";
        const underline = box.css("text-decoration") === "underline";

        const textAlign = box.css("text-align");
        const alignment = StyleEditor.ParseAlignmentFromTextAlign(textAlign);

        // If we're going to base the initial values on current actual values, we have to get the
        // margin-below and text-indent values from one of the paragraphs they actually affect.
        // I don't recall why we wanted to get the values from the box rather than from the
        // existing style rule. Trying to do so will be a problem if there are no paragraph
        // boxes in the div from which to read the value. I don't think this should happen.
        const paraBox = box.find("p");

        const marginBelowString = paraBox.css("margin-bottom");
        const paraSpacePx = parseInt(marginBelowString, 10);
        const paraSpaceEm = paraSpacePx / pxSize;
        const paraSpaceOptions = this.getParagraphSpaceOptions();
        const paraSpacing = StyleEditor.GetClosestValueInList(
            paraSpaceOptions,
            paraSpaceEm
        );

        const indentString = paraBox.css("text-indent");
        const indentNumber = parseInt(indentString, 10);
        let paraIndent = "none";
        if (indentNumber > 1) {
            paraIndent = "indented";
        } else if (indentNumber < 0) {
            paraIndent = "hanging";
        }

        let textColor = box.css("color");
        if (!textColor) textColor = "rgba(0,0,0,1.0)";

        const styleName = StyleEditor.GetStyleNameForElement(
            this.boxBeingEdited
        );
        const { hiliteTextColor, hiliteBgColor } = this.getAudioHiliteProps(
            styleName ?? ""
        );

        return {
            ptSize: ptSize.toString(),
            fontName: this.getFontNameFromTextBox(box),
            lineHeight: lineHeight,
            wordSpacing: wordSpacing,
            alignment,
            paraSpacing: paraSpacing,
            paraIndent: paraIndent,
            bold: bold,
            italic: italic,
            underline: underline,
            color: textColor,
            hiliteTextColor,
            hiliteBgColor,
            padding: box.css("padding-left")
        };
    }

    /**
     * Convert from the various text-align CSS values to the limited Alignment options we support.
     */
    private static ParseAlignmentFromTextAlign(textAlign: string): Alignment {
        switch (textAlign) {
            case "center":
                return "center";
            case "end":
                return "end";
            case "justify":
                return "justify";
            case "start":
            case "initial":
            default:
                return "start";
        }
    }

    // It's tempting to get these from fmtButton.getBoundingClientRect(), but that yields
    // an empty rectangle when it is hidden.
    fmtButtonWidth = 20;
    fmtButtonHeight = this.fmtButtonWidth;

    public AdjustFormatButton(element: Element): void {
        const scale = EditableDivUtils.getPageScale();
        const eltBounds = element.getBoundingClientRect();
        // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
        const parentBounds = element.parentElement!.getBoundingClientRect();
        const bottom = eltBounds.bottom - parentBounds.top;
        const fmtButton = document.getElementById("formatButton");

        if (!fmtButton) {
            return; // should not happen
        }
        // The quiz page is formatted with just enough space for each question/answer, so a format button in
        // the usual place on the left overlaps the text annoyingly. So for these pages we move it to the
        // right. (Not sure why that wouldn't always be better.)
        if (element.closest(".quiz")) {
            fmtButton.style.top = bottom / scale - this.fmtButtonHeight + "px";
            fmtButton.style.left = "unset";
            fmtButton.style.right = "0";
        } else if (element.closest(".bloom-textOverPicture")) {
            // This element is inside a text-over-picture element.
            fmtButton.style.top = bottom / scale + "px";
            fmtButton.style.left = -5 - this.fmtButtonWidth + "px";
        } else {
            fmtButton.style.top = bottom / scale - this.fmtButtonHeight + "px";
            const tg = fmtButton.closest(".bloom-translationGroup");
            let leftPx = 0;
            if (tg) {
                // account for any left padding the translation group may have
                const editables = Array.from(
                    tg.getElementsByClassName("bloom-editable")
                );
                const editable = editables.find(
                    x => (x as HTMLElement).offsetHeight > 0 // need to find a visible one for a meaningful offsetLeft
                );
                if (editable) leftPx = (editable as HTMLElement).offsetLeft;
            } else {
                // probably arithmetic template page, which has a numberRow instead of a TG.
                //I think this is a better algorithm anyway, but play safe and avoid dangerous change near end of 5.5
                leftPx = (eltBounds.left - parentBounds.left) / scale;
            }

            fmtButton.style.left = leftPx + "px";
        }
    }

    private updateFontControl(
        fontMetadata: IFontMetaData[],
        fontName: string
    ): void {
        ReactDOM.render(
            React.createElement(FontSelectComponent, {
                fontMetadata: fontMetadata,
                currentFontName: fontName,
                languageNumber: 0,
                onChangeFont: name => this.changeFont(name),
                // Needed to make sure the font menu that pops up is above the BloomDialog.
                // This is ugly...I'd much rather we didn't need this prop on either
                // FontSelectComponent or WinFormsStyleSelect. It would be preferable to
                // bring our z-index scheme in line with material-UIs and have the dialog
                // lower than the 1300 which is material UI's default for the popover.
                // But, see the long explanation in bloomDialog.less of why @dialogZindex
                // is 60,000. Looks like fixing that would be a project.
                // (Earlier, this high z-index was built into WinFormsStyleSelect, but
                // in other contexts, such as the Book Making tab, we need to NOT mess with
                // the z-index. See BL-11271.)
                // Another option worth considering is to make a wrapper for the FontSelectComponent
                // when it is used in this context, move the Theme management into the wrapper,
                // and let the wrapper mess with the lightTheme in the way that WinFormsStyleSelect
                // currently does. Feels more complicated, and it's also ugly to mess with a theme
                // to patch a child component. I'm not sure which is worse.
                popoverZindex: "60001"
            }),
            document.getElementById("fontSelectComponent")
        );
    }

    private uiLang: string;

    public AttachToBox(targetBox: HTMLElement) {
        this.uiLang = theOneLocalizationManager.getCurrentUILocale();

        // This method is called when the window gets focus. This may be before CkEditor has finished loading.
        // Somewhere in the course of loading, it detects editable divs that are empty except for our gear icon.
        // It decides to insert some content...typically <p><br></p>, and in doing so, replaces the gear icon div.
        // Attempts to suppress this with  config.fillEmptyBlocks, config.protectedSource,
        // config.allowedContent, and data-cke-survive did not work.
        // The only solution we have found is to postpone adding the gear icon until CkEditor has done
        // its nefarious work. The following block achieves this.
        // Enhance: this logic is roughly duplicated in toolbox.ts function doWhenCkEditorReadyCore.
        // There may be some way to refactor it into a common place, but I don't know where.
        const editorInstances = (<any>window).CKEDITOR.instances;
        // (The instances property leads to an object in which each property is an instance of CkEditor)
        let gotOne = false;
        for (const property in editorInstances) {
            const instance = editorInstances[property];
            gotOne = true;
            if (!instance.instanceReady) {
                instance.on("instanceReady", e => this.AttachToBox(targetBox));
                return;
            }
        }
        if (!gotOne) {
            // If any editable divs exist, call us again once the page gets set up with ckeditor.
            // no instance at all...if one is later created, get us invoked.
            (<any>window).CKEDITOR.on("instanceReady", e =>
                this.AttachToBox(targetBox)
            );
            return;
        }
        const oldCog = document.getElementById("formatButton");
        if (oldCog && this._previousBox == targetBox) {
            return;
        }
        if (this._previousBox != null) {
            StyleEditor.CleanupElement(this._previousBox);
        }
        if (oldCog) {
            oldCog.remove();
        }

        const styleName = StyleEditor.GetStyleNameForElement(targetBox);
        if (!styleName) {
            return;
        }

        this.xmatterMode = IsPageXMatter($(targetBox));

        this._previousBox = targetBox;

        let formatButtonFilename = "cogGrey.svg";
        const isTextOverPicture = targetBox.closest(".bloom-textOverPicture");
        if (isTextOverPicture) {
            formatButtonFilename = "cog.svg";
        }

        // Put the format button in the parent translation group. This prevents it being editable,
        // and avoids various complications; also, in WebView2, having it inside the content-editable
        // element somehow prevents ctrl-A from working (BL-12118).
        // It doesn't much matter where it goes in the parent, as it is positioned absolutely,
        // but being after the element we want it on top of at least makes it be above that element.
        // If the parent were display:block, we might be able to get it in the right place
        // (that is, near the end of targetBox) by NOT setting any of its top,right,bottom,left;
        // but the parent is display:flex so it just ends up in the top left.
        // Instead, we have to actually compute the position, and observe changes in the size of
        // targetBox that might require adjusting it.
        // As far as I know, nothing ELSE can change (while this box has focus and the button exists)
        // that would require moving it. (Moving a bubble moves the TG, so the button goes along.)
        $(targetBox).after(
            '<div id="formatButton" contenteditable="false" class="bloom-ui"><img contenteditable="false" src="' +
                this._supportFilesRoot +
                `/img/${formatButtonFilename}"></div>`
        );

        this.AdjustFormatButton(targetBox);
        if (this._observer) {
            this._observer.disconnect();
        }
        this._observer = new ResizeObserver(() =>
            this.AdjustFormatButton(targetBox)
        );
        this._observer.observe(targetBox);

        const formatButton = document.getElementById("formatButton");
        /* we removed this for BL-799, plus it was always getting in the way, once the format popup was opened
        const txt = theOneLocalizationManager.getText('EditTab.FormatDialogTip', 'Adjust formatting for style');
        editor.AddQtipToElement(formatButton, txt, 1500);
        */

        //The following commented out code works fine on Windows, but causes the program to crash
        //(disappear) on Linux when you click on the format button.
        //if (suppress.length > 0 && suppress.attr('content').toLowerCase() === 'true') {
        //    formatButton.click(function () {
        //        locmang.asyncGetText('BookEditor.FormattingDisabled', 'Sorry, Reader Templates do not allow changes to formatting.')
        //            .done(translation => {
        //                alert(translation);
        //        });
        //    });
        //    return;
        //}

        formatButton?.addEventListener("click", () =>
            this.runFormatDialog(targetBox)
        );
    }
    changePadding(padding: string) {
        if (this.ignoreControlChanges) {
            return;
        }
        const oldPaddingStr = window.getComputedStyle(this.boxBeingEdited)
            .paddingLeft;
        BubbleManager.adjustOverlaysForPaddingChange(
            this.boxBeingEdited.ownerDocument.body,
            StyleEditor.GetStyleNameForElement(this.boxBeingEdited) ?? "",
            oldPaddingStr,
            padding
        );
        const rule = this.getStyleRule(true, false);
        if (rule !== null) {
            rule.style.setProperty("padding", padding, "important");
            this.cleanupAfterStyleChange();
            RenderOverlayRoot(padding, (newPadding: string) => {
                this.changePadding(newPadding);
            });
        }
    }

    // Since both the font list popover and the FontInformationPane use the same root class, and since
    // both are well outside the dialog in the DOM, we can search the DOM for this class to determine
    // if either popover is active.
    // We have to look for a class that starts with 'MuiPopover-root' because MUI tends to add random
    // suffixes to the class names.
    // The same logic applies to popping up the color chooser dialog, except that its class name
    // starts with MuiDialog-root.
    private popoverIsUp(): boolean {
        const $body = $("body");
        return (
            $body.find("[class*='MuiPopover-root']").length > 0 ||
            $body.find("[class*='MuiDialog-root']").length > 0
        );
    }

    public closeDialog(
        event: JQueryEventObject,
        toolbar: JQuery,
        sourceDiv: HTMLElement
    ) {
        // Prevent a click from closing the base format dialog when any popover is "up"
        // or when a child dialog is open.
        if (this.popoverIsUp()) {
            return;
        }
        if (
            event.target !== toolbar.get(0) &&
            toolbar.has(event.target).length === 0 &&
            $(event.target).parent() !== toolbar &&
            toolbar.has(event.target).length === 0 &&
            toolbar.is(":visible")
        ) {
            this.removeStyleDropdown(toolbar);
            toolbar.remove();
            event.stopPropagation();
            event.preventDefault();
            sourceDiv.focus();
        }
    }

    private removeStyleDropdown(toolbar: JQuery) {
        const dropdown = toolbar.siblings("span.select2-container--open")[0];
        if (dropdown) {
            dropdown.remove(); // in case it was open when we click outside of the dialog
        }
    }

    public setupSelectControls(current: IFormattingValues, styleName: string) {
        this.populateSelect(
            this.getPointSizes(),
            current.ptSize,
            "size-select",
            true,
            true,
            99
        );
        this.populateSelect(
            this.getLineSpaceOptions(),
            current.lineHeight,
            "line-height-select",
            true,
            true
        );
        this.populateSelect(
            this.getWordSpaceOptions(),
            current.wordSpacing,
            "word-space-select",
            false,
            false
        );
        this.asyncPopulateSelect(
            "styleSelect",
            this.styles,
            this.getStylePromises(this.styles),
            styleName,
            "normal"
        );
        this.populateSelect(
            this.getParagraphSpaceOptions(),
            current.paraSpacing,
            "para-spacing-select",
            true,
            true
        );
    }

    public getButtonIds() {
        return [
            "bold",
            "italic",
            "underline",
            "position-left",
            "position-center",
            "position-right",
            "position-justify",
            "indent-none",
            "indent-indented",
            "indent-hanging"
        ];
    }

    public selectButtons(current) {
        const isRightToLeft = this.boxBeingEdited.getAttribute("dir") === "rtl";

        this.selectButton("bold", current.bold);
        this.selectButton("italic", current.italic);
        this.selectButton("underline", current.underline);
        this.selectButton(
            "position-left",
            (current.alignment === "start" && !isRightToLeft) ||
                (current.alignment === "end" && isRightToLeft)
        );
        this.selectButton("position-center", current.alignment === "center");
        this.selectButton(
            "position-right",
            (current.alignment === "end" && !isRightToLeft) ||
                (current.alignment === "start" && isRightToLeft)
        );
        this.selectButton("position-justify", current.alignment === "justify");
        this.selectButton("indent-" + current.paraIndent, true);
    }

    // Generic State Machine changes a class on the specified id from class 'state-X' to 'state-newState'
    public stateChange(id: string, newState: string) {
        const stateToAdd = "state-" + newState;
        const stateElement = $("#" + id);
        const existingClasses = stateElement.attr("class").split(/\s+/);
        $.each(existingClasses, (index, elem) => {
            if (elem.startsWith("state-")) {
                stateElement.removeClass(elem);
            }
        });
        stateElement.addClass(stateToAdd);
    }

    // Specific State Machine changes the Style section state
    public styleStateChange(newState: string) {
        if (newState === "enteringStyle" && $("#style-select-input").val()) {
            $("#create-button").removeAttr("disabled");
        } else {
            $("#create-button").attr("disabled", "true");
        }
        this.stateChange("style-group", newState);
    }

    public styleInputChanged() {
        const typedStyle = $("#style-select-input").val();
        // change state based on input
        if (typedStyle) {
            if (this.inputStyleExists()) {
                this.styleStateChange("already-exists");
                return;
            }
        }
        this.styleStateChange("enteringStyle");
    }

    public showCreateStyle() {
        this.styleStateChange("enteringStyle");
        $("#style-select-input").focus();
        return false; // prevent default click
    }

    public buttonClick(buttonDiv) {
        const button = $(buttonDiv);
        const id = button.attr("id");
        const index = id.indexOf("-");
        if (index >= 0) {
            button.addClass("selectedIcon");
            const group = id.substring(0, index);
            $(".propButton").each(function() {
                const item = $(this);
                if (
                    this !== button.get(0) &&
                    item.attr("id").startsWith(group)
                ) {
                    item.removeClass("selectedIcon");
                }
            });
        } else {
            // button is not part of a group, so must toggle
            if (button.hasClass("selectedIcon")) {
                button.removeClass("selectedIcon");
            } else {
                button.addClass("selectedIcon");
            }
        }
        // Now make it so
        if (id === "bold") {
            this.changeBold();
        } else if (id === "italic") {
            this.changeItalic();
        } else if (id === "underline") {
            this.changeUnderline();
        } else if (id.startsWith("indent")) {
            this.changeIndent();
        } else if (id.startsWith("position")) {
            this.changePosition();
        }
    }

    public selectButton(id: string, val: boolean) {
        if (val) {
            $("#" + id).addClass("selectedIcon");
        }
    }

    // The Char tab description is language-dependent when localizing, not when authoring.
    public updateLabelsWithStyleName() {
        const styleName = StyleEditor.GetBaseStyleNameForElement(
            this.boxBeingEdited
        );
        // BL-2386 This one should NOT be language-dependent; only style dependent
        // BL-5616 This also applies if the textbox's default language is '*',
        // like it is for an Arithmetic Equation.
        const tag = $(this.boxBeingEdited).attr("lang");
        if (this.shouldSetDefaultRule() || tag === "*") {
            theOneLocalizationManager
                .asyncGetText(
                    "BookEditor.DefaultForText",
                    "This formatting is the default for all text boxes with '{0}' style.",
                    "",
                    styleName
                )
                .done(translation => {
                    $("#formatCharDesc").html(translation);
                });
            return;
        }
        //BL-982 Use language name that appears on text windows
        let lang = theOneLocalizationManager.getLanguageName(tag);
        if (!lang) {
            lang = tag;
        }
        theOneLocalizationManager
            .asyncGetText(
                "BookEditor.ForTextInLang",
                "This formatting is for all {0} text boxes with '{1}' style.",
                "",
                lang,
                styleName
            )
            .done(translation => {
                $("#formatCharDesc").html(translation);
            });
    }

    // The More tab settings are never language-dependent
    public getParagraphTabDescription() {
        const styleName = StyleEditor.GetBaseStyleNameForElement(
            this.boxBeingEdited
        );
        // BL-2386 This one should NOT be language-dependent; only style dependent
        theOneLocalizationManager
            .asyncGetText(
                "BookEditor.ForText",
                "This formatting is for all text boxes with '{0}' style.",
                "",
                styleName
            )
            .done(translation => {
                $("#formatParaDesc").html(translation);
            });
    }

    // did the user type the name of an existing style?
    public inputStyleExists(): boolean {
        const typedStyle = $("#style-select-input").val();
        return this.styles.some(style => style.hasStyleId(typedStyle));
    }

    // Make a new style. Initialize to all current values. Caller should ensure it is a valid new style.
    public createStyle() {
        const typedStyle = $("#style-select-input").val();
        StyleEditor.SetStyleNameForElement(
            this.boxBeingEdited,
            typedStyle + "-style"
        );
        this.updateStyle();

        // Recommended way to insert an item into a select2 control and select it (one of the trues makes it selected)
        // See http://codepen.io/alexweissman/pen/zremOV
        const newState = new Option(typedStyle, typedStyle, true, true);
        $("#styleSelect")
            .append(newState)
            .trigger("change");
        // Ensure we know this style in the future.  See http://issues.bloomlibrary.org/youtrack/issue/BL-4438.
        this.styles.push(new FormattingStyle(typedStyle, typedStyle));

        // This control has been hidden, but the user could show it again.
        // And showing it does not run the duplicate style check, since we expect it to be empty
        // at that point, so that made a loophole for creating duplicate styles.
        $("#style-select-input").val("");
    }

    public updateStyle() {
        this.changeSize();
        this.changeLineheight();
        this.changeWordSpace();
        this.changeIndent();
        this.changeBold();
        this.changeItalic();
        this.changeUnderline();
        this.changeParaSpacing();
        this.changePosition();
        const colorButton = $("#colorSelectButton");
        const style = getComputedStyle(colorButton[0]);
        this.changeColor(style.backgroundColor);
        this.styleStateChange("initial"); // go back to initial state so user knows it worked
    }

    public asyncPopulateSelect(
        selectId: string,
        styles: FormattingStyle[],
        localizedNamePromises: JQueryPromise<string>[],
        current: string,
        defaultChoice: string
    ) {
        // So if the element doesn't exist, exit quickly.
        if (!$("#" + selectId)) {
            return;
        }
        if (!current || current === "") {
            current = defaultChoice;
        }

        // Inside of here we use the "all" function to ensure that nothing happens until all
        // of the promises return.
        $.when(this.all(localizedNamePromises)).then(
            (allPromiseResults: string[]) => {
                let options = "";
                allPromiseResults.forEach((result, i) => {
                    styles[i].localizedName = result;
                });
                const sortedStyles = this.sortByLocalizedName(styles);
                for (let i = 0; i < sortedStyles.length; i++) {
                    let selected: string = "";
                    if (current === sortedStyles[i].styleId) {
                        selected = " selected";
                    }
                    options +=
                        '<option value="' +
                        sortedStyles[i].styleId +
                        '"' +
                        selected +
                        ">" +
                        sortedStyles[i].getLocalizedName() +
                        "</option>";
                }
                $("#" + selectId).html(options);
            }
        );
    }

    public sortByLocalizedName(styles: FormattingStyle[]): FormattingStyle[] {
        return styles.sort((s1: FormattingStyle, s2: FormattingStyle) => {
            if (s1.getLocalizedName() > s2.getLocalizedName()) {
                return 1;
            }
            if (s1.getLocalizedName() < s2.getLocalizedName()) {
                return -1;
            }
            return 0;
        });
    }

    public stringSort(items: string[]): string[] {
        return items.sort((a: string, b: string) => {
            if (a > b) {
                return 1;
            }
            if (a < b) {
                return -1;
            }
            return 0;
        });
    }

    public populateSelect(
        items: string[],
        current: string,
        id: string,
        doSort: boolean,
        useNumericSort: boolean,
        maxlength?: number
    ) {
        // Rather than selectively call this method for only those select elements which need
        // to be initialized, we call it for all of them. That makes the calling code a little simpler.

        // So if the element doesn't exist, exit quickly.
        if (!$("#" + id)) {
            return;
        }

        let options = "";
        if (current && items.indexOf(current.toString()) === -1) {
            //we have a custom point size, so make that an option in addition to the standard ones
            items.push(current.toString());
        }
        let sortedItems: string[];
        if (doSort) {
            if (useNumericSort) {
                sortedItems = items.sort((a: string, b: string) => {
                    return Number(a) - Number(b);
                });
            } else {
                sortedItems = this.stringSort(items);
            }
        } else {
            sortedItems = items;
        }
        if (!current) {
            current = items[0];
        }

        for (let i = 0; i < sortedItems.length; i++) {
            let selected: string = "";
            if (current.toString() === sortedItems[i]) {
                // toString() is necessary to match point size string
                selected = " selected";
            }
            let text = sortedItems[i];
            if (useNumericSort) {
                // get localized version (e.g. with different decimal separator)
                text = parseFloat(text).toLocaleString(this.uiLang);
            }
            if (maxlength && text.length > maxlength) {
                text = text.substring(0, maxlength) + "...";
            }
            options +=
                '<option value="' +
                sortedItems[i] +
                '"' +
                selected +
                ">" +
                text +
                "</option>";
        }
        $("#" + id).html(options);
    }

    public changeBold() {
        if (this.ignoreControlChanges) {
            return;
        }
        let rule = this.getStyleRule(false);
        const val = $("#bold").hasClass("selectedIcon");
        if (rule != null) {
            rule.style.setProperty(
                "font-weight",
                val ? "bold" : "normal",
                "important"
            );
        }
        if (this.shouldSetDefaultRule()) {
            rule = this.getStyleRule(true);
            if (rule != null)
                rule.style.setProperty(
                    "font-weight",
                    val ? "bold" : "normal",
                    "important"
                );
        }
        this.cleanupAfterStyleChange();
    }

    public changeItalic() {
        if (this.ignoreControlChanges) {
            return;
        }
        let rule = this.getStyleRule(false);
        const val = $("#italic").hasClass("selectedIcon");
        if (rule != null)
            rule.style.setProperty(
                "font-style",
                val ? "italic" : "normal",
                "important"
            );
        if (this.shouldSetDefaultRule()) {
            rule = this.getStyleRule(true);
            if (rule != null)
                rule.style.setProperty(
                    "font-style",
                    val ? "italic" : "normal",
                    "important"
                );
        }
        this.cleanupAfterStyleChange();
    }

    public changeUnderline() {
        if (this.ignoreControlChanges) {
            return;
        }
        let rule = this.getStyleRule(false);
        const val = $("#underline").hasClass("selectedIcon");
        if (rule != null)
            rule.style.setProperty(
                "text-decoration",
                val ? "underline" : "none",
                "important"
            );
        if (this.shouldSetDefaultRule()) {
            rule = this.getStyleRule(true);
            if (rule != null)
                rule.style.setProperty(
                    "text-decoration",
                    val ? "underline" : "none",
                    "important"
                );
        }
        this.cleanupAfterStyleChange();
    }

    public changeFont(fontname: string) {
        if (this.ignoreControlChanges) {
            return;
        }
        const rule = this.getStyleRule(false);
        if (rule != null) {
            rule.style.setProperty("font-family", fontname, "important");
            this.cleanupAfterStyleChange();
        }
    }

    public changeColor(color: string) {
        if (this.ignoreControlChanges) {
            return;
        }
        const rule = this.getStyleRule(false);
        if (rule != null) {
            rule.style.setProperty("color", color);
            this.cleanupAfterStyleChange();
        }
        this.setColorButtonColor("colorSelectButton", color);
    }

    // Always updates the UI to show the specified values for audio-hiliting props,
    // and if ignoreControlChanges is false (i.e., the change isn't from a style switch)
    // also updates the style definition in the DOM.
    public changeHiliteProps(
        hiliteTextColor: string | undefined, // text color when hilited (null or empty to not specify)
        hiliteBgColor: string, // bg color when hilited
        color?: string // ordinary text color
    ) {
        if (!color) {
            color = this.getFormatValues().color;
        }
        const styleName = StyleEditor.GetStyleNameForElement(
            this.boxBeingEdited
        );
        const uiStyleName = StyleEditor.GetBaseStyleNameForElement(
            this.boxBeingEdited
        );
        // The only way I know to get the new hilight color in there is to re-render
        // completely.
        RenderRoot(
            uiStyleName ?? "",
            color,
            hiliteTextColor,
            hiliteBgColor,
            (textColor, bgColor) => this.changeHiliteProps(textColor, bgColor)
        );
        if (this.ignoreControlChanges) {
            return;
        }

        if (styleName) {
            this.putAudioHiliteRulesInDom(
                styleName,
                hiliteTextColor,
                hiliteBgColor
            );
        }
    }

    changeOverlayProps(padding: string) {
        if (this.ignoreControlChanges) {
            return;
        }
        // const styleName = StyleEditor.GetStyleNameForElement(
        //     this.boxBeingEdited
        // );
        RenderOverlayRoot(padding, (newPadding: string) => {
            this.changePadding(newPadding);
        });
        // if (styleName) {
        //     this.putOverlayRulesInDom(styleName, padding);
        // }
    }

    // putOverlayRulesInDom(styleName: string, padding: string) {
    //     // TODO
    // }

    private setColorButtonColor(id: string, color: string) {
        const colorButton = document.getElementById(id);
        colorButton?.setAttribute("style", `background-color:${color}`);
    }

    // Return true if font-tab changes (other than font family) for the current element should be applied
    // to the default rule as well as a language-specific rule.
    // Currently this requires that the element's language is the project's first language, which happens
    // to be available to us through the injected setting 'languageForNewTextBoxes'.
    // (If that concept diverges from 'the language whose style settings are the default' we may need to
    // inject a distinct value.)
    public shouldSetDefaultRule() {
        const target = this.boxBeingEdited;
        // GetSettings is injected into the page by C#.
        if ($(target).attr("lang") !== GetSettings().languageForNewTextBoxes) {
            return false;
        }
        // We need to some way of detecting that we don't want to set
        // default rule for blocks like the main title, where factory rules do things like
        // making .bookTitle.bloom-contentNational1 120% and .bookTitle.bloom-content1 250%.
        return !$(target).hasClass("bloom-nodefaultstylerule");
    }

    public changeSize() {
        if (this.ignoreControlChanges) {
            return;
        }

        const fontSize = $("#size-select").val();
        const units = "pt";
        const sizeString = fontSize.toString();
        if (parseInt(sizeString, 10) < this.MIN_FONT_SIZE) {
            return; // should not be possible?
        }
        this.changeSizeInternal(
            sizeString + units,
            this.shouldSetDefaultRule()
        );
        this.cleanupAfterStyleChange();
    }

    public changeSizeInternal(
        newSize: string,
        shouldSetDefaultRule: boolean
    ): void {
        // Always set the value in the language-specific rule
        let rule = this.getStyleRule(false);
        if (rule != null)
            rule.style.setProperty("font-size", newSize, "important");
        if (shouldSetDefaultRule) {
            rule = this.getStyleRule(true);
            if (rule != null)
                rule.style.setProperty("font-size", newSize, "important");
        }
    }

    public changeLineheight() {
        if (this.ignoreControlChanges) {
            return;
        }
        const lineHeight = $("#line-height-select").val();
        let rule = this.getStyleRule(false);
        if (rule != null)
            rule.style.setProperty("line-height", lineHeight, "important");
        if (this.shouldSetDefaultRule()) {
            rule = this.getStyleRule(true);
            if (rule != null)
                rule.style.setProperty("line-height", lineHeight, "important");
        }
        this.cleanupAfterStyleChange();
    }

    public changeWordSpace() {
        //careful here: the labels we get are localized, so you can't just compare to English ones (BL-3527)
        if (this.ignoreControlChanges) {
            return;
        }

        const chosenIndex = $("#word-space-select option:selected").index();
        let wordSpace;
        switch (chosenIndex) {
            case 1:
                wordSpace = "5pt";
                break;
            case 2:
                wordSpace = "10pt";
                break;
            default:
                wordSpace = "normal";
        }
        let rule = this.getStyleRule(false);
        if (rule != null)
            rule.style.setProperty("word-spacing", wordSpace, "important");
        if (this.shouldSetDefaultRule()) {
            rule = this.getStyleRule(true);
            if (rule != null)
                rule.style.setProperty("word-spacing", wordSpace, "important");
        }
        this.cleanupAfterStyleChange();
    }
    public changeParaSpacing() {
        if (this.ignoreControlChanges) {
            return;
        }
        const paraSpacing = $("#para-spacing-select").val() + "em";
        const rule = this.getStyleRule(true, true);
        if (rule != null) {
            rule.style.setProperty("margin-bottom", paraSpacing, "important");
            this.cleanupAfterStyleChange();
        }
    }

    /**
     * Sets the alignment, using "start", "center", "justify", or "end"
     * Clicking the align left button will cause the text to go to the left,
     * meaning "start" for left-to-right boxes and "end" for right-to-left boxes
     */
    public changePosition() {
        if (this.ignoreControlChanges) {
            return;
        }

        const whichButtonClicked = this.getWhichAlignmentButtonClicked();

        let position = "start";
        if (whichButtonClicked === "center") {
            position = "center";
        } else if (whichButtonClicked === "justify") {
            position = "justify";
        } else {
            const textBoxDirection = this.boxBeingEdited.getAttribute("dir");
            const isRightToLeft = textBoxDirection === "rtl";

            if (
                (whichButtonClicked === "left" && !isRightToLeft) ||
                (whichButtonClicked === "right" && isRightToLeft)
            ) {
                position = "start";
            } else {
                position = "end";
            }
        }

        const rule = this.getStyleRule(true, undefined);
        if (rule !== null) {
            rule.style.setProperty("text-align", position, "important");

            this.cleanupAfterStyleChange();
        }
    }

    private getWhichAlignmentButtonClicked():
        | "left"
        | "center"
        | "right"
        | "justify" {
        const positionCenterButton = document.getElementById("position-center");
        if (
            positionCenterButton &&
            positionCenterButton.classList.contains("selectedIcon")
        ) {
            return "center";
        }

        const positionJustifyButton = document.getElementById(
            "position-justify"
        );
        if (
            positionJustifyButton &&
            positionJustifyButton.classList.contains("selectedIcon")
        ) {
            return "justify";
        }

        const positionRightButton = document.getElementById("position-right");
        if (
            positionRightButton &&
            positionRightButton.classList.contains("selectedIcon")
        ) {
            return "right";
        } else {
            return "left";
        }
    }

    public changeIndent() {
        if (this.ignoreControlChanges) {
            return;
        }
        const rule = this.getStyleRule(true, true); // rule that is language-independent for child paras
        if (rule == null) return;
        if ($("#indent-none").hasClass("selectedIcon")) {
            // It's tempting to remove the property. However, we apply normal-style as a class to
            // parent bloom-translationGroup elements so everything inherits from it by default.
            // If we just remove these properties from a rule for some other style, no other
            // style will be able to override a 'normal-style' indent.
            rule.style.setProperty("text-indent", "0", "important");
            rule.style.setProperty("margin-left", "0", "important");
        } else if ($("#indent-indented").hasClass("selectedIcon")) {
            rule.style.setProperty("text-indent", "20pt", "important");
            rule.style.setProperty("margin-left", "0", "important");
        } else if ($("#indent-hanging").hasClass("selectedIcon")) {
            rule.style.setProperty("text-indent", "-20pt", "important");
            rule.style.setProperty("margin-left", "20pt", "important");
        }

        this.cleanupAfterStyleChange();
    }

    public getSettings(ruleInput: string): string[] {
        const index1 = ruleInput.indexOf("{");
        let rule = ruleInput;
        if (index1 >= 0) {
            rule = rule.substring(index1 + 1, rule.length);
            rule = rule.replace("}", "").trim();
        }
        return rule.split(";");
    }

    public selectStyle() {
        const oldValues = this.getFormatValues();
        const style = $("#styleSelect").val();
        $("#style-select-input").val(""); // we've chosen a style from the list, so we aren't creating a new one.
        StyleEditor.SetStyleNameForElement(
            this.boxBeingEdited,
            style + "-style"
        );
        const predefined = this.getMissingPredefinedStyle(style + "-style");
        if (predefined) {
            // doesn't exist in user-defined yet; need to copy it there
            // (so it works even if from a stylesheet not part of the book)
            // and make defined settings !important so they win over anything else.
            const rule = this.getStyleRule(true);
            const settings = this.getSettings(predefined.cssText);
            for (let j = 0; j < settings.length; j++) {
                const parts = settings[j].split(":");
                if (parts.length !== 2) {
                    continue; // often a blank item after last semi-colon
                }
                const selector = parts[0].trim();
                let val = parts[1].trim();
                const index2 = val.indexOf("!");
                if (index2 >= 0) {
                    val = val.substring(0, index2);
                }
                // per our standard convention, font-family is only ever specified for a specific language.
                // If we're applying a style, we're in author mode, and all other settings apply to all languages.
                // Even if we weren't in author mode, the factory definition of a style should be language-neutral,
                // so we'd want to insert it into our book that way.
                if (selector === "font-family") {
                    const languageSpecificRule = this.getStyleRule(false);
                    if (languageSpecificRule != null)
                        languageSpecificRule.style.setProperty(
                            selector,
                            val,
                            "important"
                        );
                } else {
                    // review: may be desirable to do something if val is not one of the values
                    // we can generate, or just possibly if selector is not one of the ones we manipulate.
                    if (rule != null)
                        rule.style.setProperty(selector, val, "important");
                }
            }
        }
        // Now update all the controls to reflect the effect of applying this style.
        this.UpdateControlsToReflectAppliedStyle(oldValues.fontName);
    }

    public UpdateControlsToReflectAppliedStyle(oldFontName: string) {
        const current = this.getFormatValues();
        this.ignoreControlChanges = true;

        // IF the new style changed fonts, we need to reset the font control
        if (oldFontName !== current.fontName) {
            get("fonts/metadata", result => {
                const fontMetadata: IFontMetaData[] = result.data;
                this.updateFontControl(fontMetadata, current.fontName);
            });
        }
        this.setValueAndUpdateSelect2Control("size-select", current.ptSize);
        this.setValueAndUpdateSelect2Control(
            "line-height-select",
            current.lineHeight
        );
        this.setValueAndUpdateSelect2Control(
            "word-space-select",
            current.wordSpacing
        );
        this.setValueAndUpdateSelect2Control(
            "para-spacing-select",
            current.paraSpacing
        );
        const buttonIds = this.getButtonIds();
        for (let i = 0; i < buttonIds.length; i++) {
            $("#" + buttonIds[i]).removeClass("selectedIcon");
        }
        this.selectButtons(current);
        this.setColorButtonColor("colorSelectButton", current.color);
        this.changeHiliteProps(current.color, current.hiliteBgColor);
        this.changeOverlayProps(current.padding);
        this.ignoreControlChanges = false;
        this.cleanupAfterStyleChange();
    }

    // Doc indicates this is the correct way to programmatically select an item with select2.
    // Previous comments indicated this was buggy and referenced BL-2324, which appears to be
    // the wrong issue number; I think it should have been BL-3371. However, I suspect the problem
    // was actually neglecting to prepend # before the id. The old solution left the problem BL-3422
    // which was eventually fixed by changing createStyle so that it doesn't need to use this
    // method at all. As far as I can tell, doing this works.
    public setValueAndUpdateSelect2Control(id: string, value: string) {
        $("#" + id)
            .val(value)
            .trigger("change");
    }

    public getStyleRule(
        ignoreLanguage: boolean,
        forChildPara?: boolean
    ): CSSStyleRule | null {
        const target = this.boxBeingEdited;
        const styleName = StyleEditor.GetStyleNameForElement(target);
        if (!styleName) {
            return null; // bizarre, since we put up the dialog
        }
        const langAttrValue = StyleEditor.GetLangValueOrNull(target);
        return this.GetOrCreateRuleForStyle(
            styleName,
            langAttrValue,
            ignoreLanguage,
            forChildPara
        );
    }

    public cleanupAfterStyleChange() {
        const target = this.boxBeingEdited;
        const styleName = StyleEditor.GetStyleNameForElement(target);
        if (!styleName) {
            return; // bizarre, since we put up the dialog
        }
        OverflowChecker.MarkOverflowInternal(target);
        this.updateLabelsWithStyleName();
        this.getParagraphTabDescription();

        SetupThingsSensitiveToStyleChanges(
            $(this.boxBeingEdited).closest(".bloom-page")[0]
        );
    }

    // Remove any additions we made to the element for the purpose of UI alone
    public static CleanupElement(element) {
        const $el = $(element);
        $el.find(".bloom-ui").each(function() {
            $(this).remove();
        });

        //stop watching the scrolling event we used to keep the formatButton at the bottom
        $el.off("scroll");
    }

    public launchColorPicker(buttonColor: string) {
        StyleEditor.showColorPicker(buttonColor, this.textColorTitle, color =>
            this.changeColor(color)
        );
    }

    public static showColorPicker(
        initialColor: string,
        title: string,
        onChange: (s: string) => void
    ) {
        const colorPickerDialogProps: ISimpleColorPickerDialogProps = {
            transparency: false,
            localizedTitle: title,
            initialColor: initialColor,
            palette: BloomPalette.Text,
            onChange: onChange,
            // eslint-disable-next-line @typescript-eslint/no-empty-function
            onInputFocus: () => {}
        };
        showSimpleColorPickerDialog(colorPickerDialogProps);
    }

    public static isStyleDialogOpen(): boolean {
        // The #format-toolbar element is instantiated on the page only when the
        // dialog is showing.
        const page = parent.window.document.getElementById("page");
        const contentWindow = page
            ? (<HTMLIFrameElement>page).contentWindow
            : null;

        // possibly unit-testing
        if (!contentWindow) {
            const formatDialog = $(".bloom-page").find("#format-toolbar");
            return formatDialog.length > 0;
        }
        const formatDialog = $("body", contentWindow.document).find(
            "#format-toolbar"
        );
        return formatDialog.length > 0;
    }

    public runFormatDialog(targetBox: HTMLElement) {
        // BL-2476: Readers made from BloomPacks should have the formatting dialog disabled
        const suppress = $(document).find('meta[name="lockFormatting"]');
        const noFormatChange =
            suppress.length > 0 &&
            suppress.attr("content").toLowerCase() === "true";
        // Using axios directly because bloomApi does not support combining multiple promises with .all
        wrapAxios(
            axios
                .all([
                    axios.get("/bloom/api/fonts/metadata"),
                    axios.get("/bloom/bookEdit/StyleEditor/StyleEditor.html"),
                    theOneLocalizationManager.getTextInUiLanguageAsync(
                        "EditTab.Toolbox.ComicTool.Options.TextColor",
                        "Text Color"
                    )
                ])
                .then(results => {
                    const fontMetadata: IFontMetaData[] = results[0].data;
                    const html = results[1].data;
                    this.textColorTitle = results[2].data.text;

                    this.boxBeingEdited = targetBox;
                    const styleName = StyleEditor.GetBaseStyleNameForElement(
                        targetBox
                    );
                    const current = this.getFormatValues();
                    this.styles = this.getFormattingStyles();
                    if (
                        styleName != null &&
                        this.styles.every(
                            style => !style.hasStyleId(styleName as string)
                        )
                    ) {
                        this.styles.push(
                            new FormattingStyle(
                                styleName,
                                this.getDisplayName(styleName)
                            )
                        );
                    }

                    $("#format-toolbar").remove(); // in case there's still one somewhere else
                    $("body").append(html);

                    if (noFormatChange) {
                        $("#format-toolbar").addClass("formattingDisabled");
                    } else {
                        // The tab library doesn't allow us to put other class names on the tab-page,
                        // so we are doing it this way rather than the approach of using css to hide
                        // tabs based on class names.
                        if (this.xmatterMode) {
                            $("#style-page").remove();
                        }
                        // Show the overlay tab only if the box being edited is in an overlay
                        if (
                            !this.boxBeingEdited.closest(
                                ".bloom-textOverPicture"
                            )
                        ) {
                            document.getElementById("overlay-page")?.remove();
                        }

                        const visibleTabs = $(".tab-page:visible");
                        if (visibleTabs.length === 1) {
                            // When there is only one tab, we want to hide the tab itself.
                            $(visibleTabs[0])
                                .find("h2.tab")
                                .remove();
                        }

                        this.setupSelectControls(
                            current,
                            styleName ? styleName : ""
                        );
                    }

                    //make some select boxes permit custom values
                    $(".allowCustom").select2({
                        tags: true //this is weird, we're not really doing tags, but this is how you get to enable typing
                    });
                    $("select:not(.allowCustom)").select2({
                        tags: false,
                        minimumResultsForSearch: -1 // result is that no search box is shown
                    });

                    const toolbar = $("#format-toolbar");
                    toolbar.find("*[data-i18n]").localize();
                    toolbar.draggable({
                        distance: 10,
                        scroll: false,
                        containment: $("html")
                    });
                    toolbar.draggable("disable"); // until after we make sure it's in the Viewport
                    toolbar.css("opacity", 1.0);
                    RenderRoot(
                        styleName ?? "",
                        current.color,
                        current.hiliteTextColor,
                        current.hiliteBgColor,
                        (textColor, bgColor) =>
                            this.changeHiliteProps(textColor, bgColor)
                    );

                    RenderOverlayRoot(current.padding, (newPadding: string) => {
                        this.changePadding(newPadding);
                    });

                    if (!noFormatChange) {
                        this.updateFontControl(fontMetadata, current.fontName);
                        this.updateLabelsWithStyleName();

                        this.AddQtipToElement(
                            $("#fontSelectComponent"),
                            theOneLocalizationManager.getText(
                                "EditTab.FormatDialog.FontFaceToolTip",
                                "Change the font face"
                            ),
                            1500
                        );
                        $("#size-select").change(() => {
                            this.changeSize();
                        });
                        this.AddQtipToElement(
                            $("#size-select"),
                            theOneLocalizationManager.getText(
                                "EditTab.FormatDialog.FontSizeToolTip",
                                "Change the font size"
                            ),
                            1500
                        );
                        $("#line-height-select").change(() => {
                            this.changeLineheight();
                        });
                        this.AddQtipToElement(
                            $("#line-height-select").parent(),
                            theOneLocalizationManager.getText(
                                "EditTab.FormatDialog.LineSpacingToolTip",
                                "Change the spacing between lines of text"
                            ),
                            1500
                        );
                        $("#word-space-select").change(() => {
                            this.changeWordSpace();
                        });
                        this.AddQtipToElement(
                            $("#word-space-select").parent(),
                            theOneLocalizationManager.getText(
                                "EditTab.FormatDialog.WordSpacingToolTip",
                                "Change the spacing between words"
                            ),
                            1500
                        );
                        this.getParagraphTabDescription();
                        if (!this.xmatterMode) {
                            $("#styleSelect").change(() => {
                                this.selectStyle();
                            });
                            (<alphanumInterface>(
                                $("#style-select-input")
                            )).alphanum({
                                allowSpace: false,
                                preventLeadingNumeric: true
                            });
                            // don't use .change() here, as it only fires on loss of focus
                            $("#style-select-input").on("input", () => {
                                this.styleInputChanged();
                            });
                            // Here I'm taking advantage of JS by pushing an extra field into an object whose declaration does not allow it,
                            // so typescript checking just has to be worked around. This enables a hack in jquery.alphanum.js.
                            (<any>(
                                $("#style-select-input").get(0)
                            )).trimNotification = () => {
                                this.styleStateChange("invalid-characters");
                            };
                            $("#show-createStyle").click(event => {
                                event.preventDefault();
                                this.showCreateStyle();
                                return false;
                            });
                            $("#create-button").click(() => {
                                this.createStyle();
                            });
                        }
                        const buttonIds = this.getButtonIds();
                        for (let i = 0; i < buttonIds.length; i++) {
                            const button = $("#" + buttonIds[i]);

                            // Note: Use arrow function so that "this" refers to the right thing.
                            // Otherwise, clicking on Gear Icon's Bold All/Italics All/etc. will throw exception because "this" doesn't refer to the right thing.
                            button.click(() => {
                                this.buttonClick(button);
                            });
                            button.addClass("propButton");
                        }
                        $("#para-spacing-select").change(() => {
                            this.changeParaSpacing();
                        });
                        this.setColorButtonColor(
                            "colorSelectButton",
                            current.color
                        );
                        // (The hiliting color buttons are updated by re-rendering the control above.)
                        const colorButton = $("#colorSelectButton");
                        colorButton?.click(() => {
                            const style = getComputedStyle(colorButton[0]);
                            const backgroundColor = style.backgroundColor;
                            this.launchColorPicker(backgroundColor);
                        });

                        this.selectButtons(current);
                        new WebFXTabPane($("#tabRoot").get(0), false);
                    }
                    const orientOnButton = $("#formatButton");
                    EditableDivUtils.positionDialogAndSetDraggable(
                        toolbar,
                        orientOnButton
                    );
                    toolbar.draggable("enable");

                    $("html").off("click.toolbar");
                    $("html").on("click.toolbar", event =>
                        this.closeDialog(event, toolbar, targetBox)
                    );
                    toolbar.on("click.toolbar", event => {
                        // this stops an event inside the dialog from propagating to the html element, which would close the dialog
                        event.stopPropagation();
                    });
                })
        );
    }
}
