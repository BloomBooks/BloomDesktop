/// <reference path="../../typings/bundledFromTSC.d.ts"/>
/// <reference path="../../typings/jquery/jquery.d.ts" />
/// <reference path="../../typings/select2/select2.d.ts" />
/// <reference path="../../lib/localizationManager/localizationManager.ts" />
/// <reference path="../../lib/jquery.i18n.custom.ts" />
/// <reference path="../../lib/misc-types.d.ts" />
/// <reference path="../../lib/jquery.alphanum.d.ts"/>
/// <reference path="../js/toolbar/toolbar.d.ts"/>
/// <reference path="../js/collectionSettings.d.ts"/>
/// <reference path="../OverflowChecker/OverflowChecker.ts"/>

import "../../node_modules/select2/dist/js/select2.js";

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import OverflowChecker from "../OverflowChecker/OverflowChecker";
import {
    GetDifferenceBetweenHeightAndParentHeight,
    IsPageXMatter
} from "../js/bloomEditing";
import "../../lib/jquery.alphanum";
import axios from "axios";
import { BloomApi } from "../../utils/bloomApi";
import { EditableDivUtils } from "../js/editableDivUtils";

declare function GetSettings(): any; //c# injects this
declare function WebFxTabPane(
    element: HTMLElement,
    useCookie: boolean,
    callback: any
): any; // from tabpane, from a <script> tag

interface IFormattingValues {
    ptSize: string;
    fontName: string;
    lineHeight: string;
    wordSpacing: string;
    center: boolean;
    paraSpacing: string;
    paraIndent: string;
    bold: boolean;
    italic: boolean;
    underline: boolean;
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
        return this.styleId.toLowerCase() == name.toLowerCase();
    }

    public getLocalizedName(): string {
        // null-coalesce operator would be handy here.
        return this.localizedName
            ? this.englishDisplayName
            : this.localizedName;
    }
}

export default class StyleEditor {
    private _previousBox: Element;
    private _supportFilesRoot: string;
    private MIN_FONT_SIZE: number = 7;
    private boxBeingEdited: HTMLElement;
    private ignoreControlChanges: boolean;
    private styles: FormattingStyle[];
    private authorMode: boolean; // true if authoring (rather than translating)
    private xmatterMode: boolean; // true if we are in xmatter (and shouldn't change fixed style names)

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

    // obsolete?
    public MakeBigger(target: HTMLElement) {
        this.ChangeSize(target, 2);
        $("div.bloom-editable, textarea").qtipSecondary("reposition");
    }
    // obsolete?
    public MakeSmaller(target: HTMLElement) {
        this.ChangeSize(target, -2);
        $("div.bloom-editable, textarea").qtipSecondary("reposition");
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

    private static SetStyleNameForElement(
        target: HTMLElement,
        newStyle: string
    ) {
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

    // obsolete?
    private ChangeSize(target: HTMLElement, change: number) {
        const styleName = StyleEditor.GetStyleNameForElement(target);
        if (!styleName) {
            return;
        }
        const fontSize = this.GetCalculatedFontSizeInPoints(target);
        const langAttrValue = StyleEditor.GetLangValueOrNull(target);
        const rule: CSSStyleRule | null = this.GetOrCreateRuleForStyle(
            styleName,
            langAttrValue,
            this.authorMode
        );
        const units = "pt";
        const sizeString = (fontSize + change).toString();
        if (parseInt(sizeString, 10) < this.MIN_FONT_SIZE) {
            return; // too small, quietly don't do it!
        }
        if (rule != null)
            rule.style.setProperty(
                "font-size",
                sizeString + units,
                "important"
            );
        OverflowChecker.MarkOverflowInternal(target);

        // alert("New size rule: " + rule.cssText);
        // Now update tooltip
        const toolTip = this.GetToolTip(target, styleName);
        this.AddQtipToElement($("#formatButton"), toolTip);
    }

    public GetCalculatedFontSizeInPoints(target: HTMLElement): number {
        const sizeInPx = $(target).css("font-size");
        return this.ConvertPxToPt(parseInt(sizeInPx, 10));
    }

    public ChangeSizeAbsolute(target: HTMLElement, newSize: number) {
        const styleName = StyleEditor.GetStyleNameForElement(target); // finds 'x-style' class or null
        if (!styleName) {
            alert(
                "ChangeSizeAbsolute called on an element with invalid style class."
            );
            return;
        }
        if (newSize < this.MIN_FONT_SIZE) {
            // newSize is expected to come from a combobox entry by the user someday
            alert("ChangeSizeAbsolute called with too small a point size.");
            return;
        }
        const langAttrValue = StyleEditor.GetLangValueOrNull(target);
        const rule: CSSStyleRule | null = this.GetOrCreateRuleForStyle(
            styleName,
            langAttrValue,
            this.authorMode
        );
        const units = "pt";
        const sizeString: string = newSize.toString();
        if (rule != null)
            rule.style.setProperty(
                "font-size",
                sizeString + units,
                "important"
            );
        // Now update tooltip
        const toolTip = this.GetToolTip(target, styleName);
        this.AddQtipToElement($("#formatButton"), toolTip);
    }

    // Get the names that should be offered in the styles combo box.
    // Basically any defined rules for classes that end in -style.
    // Only the last class in a sequence is used; this lets us predefine
    // styles like DIV.bloom-editing.Heading1 and make their selectors specific enough to work,
    // but not impossible to override with a custom definition.
    public getFormattingStyles(): FormattingStyle[] {
        const styles: FormattingStyle[] = [];
        for (let i = 0; i < document.styleSheets.length; i++) {
            const sheet = <StyleSheet>(<any>document.styleSheets[i]);
            const rules: CSSRuleList = (<any>sheet).cssRules;
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

    // Get the existing rule for the specified style.
    // Will return null if the style has no definition, OR if it already has a user-defined version
    public getPredefinedStyle(target: string): CSSRule | null {
        let result: CSSRule | null = null;
        for (let i = 0; i < document.styleSheets.length; i++) {
            const sheet = <StyleSheet>(<any>document.styleSheets[i]);
            const rules: CSSRuleList = (<any>sheet).cssRules;
            if (rules) {
                for (let j = 0; j < rules.length; j++) {
                    const index = rules[j].cssText.indexOf("{");
                    if (index === -1) {
                        continue;
                    }
                    const label = rules[j].cssText.substring(0, index).trim();
                    if (label.indexOf(target) >= 0) {
                        // We have a rule for our target!
                        // Is this the user-defined stylesheet?
                        if (
                            (<StyleSheet>(
                                (<any>document.styleSheets[i]).ownerNode
                            )).title === "userModifiedStyles"
                        ) {
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
        const styleSheet = this.GetOrCreateUserModifiedStyleSheet();
        if (styleSheet == null) {
            return null;
        }

        let ruleList: CSSRuleList = styleSheet.cssRules;
        if (ruleList == null) {
            ruleList = new CSSRuleList();
        }

        let styleAndLang = styleName;
        // if we are authoring a book, style changes should apply to all translations of it
        // if we are translating, changes should only apply to this language.
        // a downside of this is that when authoring in multiple languages, to get a different
        // appearance for different languages a different style must be created.
        if (!ignoreLanguage) {
            if (langAttrValue && langAttrValue.length > 0) {
                styleAndLang = styleName + '[lang="' + langAttrValue + '"]';
            } else {
                styleAndLang = styleName + ":not([lang])";
            }
        }

        if (forChildParas) {
            styleAndLang += " > p";
        }

        const lookFor = styleAndLang.toLowerCase();

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
        styleSheet.insertRule("." + styleAndLang + " { }", ruleList.length);

        return <CSSStyleRule>ruleList[ruleList.length - 1]; //new guy is last
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
        const results: string[] = [];
        const promises: JQueryPromise<string>[] = [];

        styles.forEach(formattingStyle => {
            const completeStyleName =
                "EditTab.FormatDialog.DefaultStyles." +
                formattingStyle.styleId +
                "-style";
            promises.push(theOneLocalizationManager.asyncGetText(
                completeStyleName,
                formattingStyle.englishDisplayName,
                ""
            ) as JQueryPromise<string>);
        });
        return promises;
    }

    public getParagraphSpaceOptions() {
        return ["0", "0.5", "0.75", "1", "1.25"];
    }

    // Returns an object giving the current selection for each format control.
    public getFormatValues(): IFormattingValues {
        const box = $(this.boxBeingEdited);
        const sizeString = box.css("font-size");
        const pxSize = parseInt(sizeString, 10);
        let ptSize = this.ConvertPxToPt(pxSize, false);
        const sizes = this.getPointSizes();

        ptSize = Math.round(ptSize);
        let fontName = box.css("font-family");
        if (fontName[0] === "'" || fontName[0] === '"') {
            fontName = fontName.substring(1, fontName.length - 1); // strip off quotes
        }

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
        const center = box.css("text-align") === "center";

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

        return {
            ptSize: ptSize.toString(),
            fontName: fontName,
            lineHeight: lineHeight,
            wordSpacing: wordSpacing,
            center: center,
            paraSpacing: paraSpacing,
            paraIndent: paraIndent,
            bold: bold,
            italic: italic,
            underline: underline
        };
    }

    public AdjustFormatButton(element: Element): void {
        // Bizarrely, bottom:0 means to place it where the bottom of the content would be
        // if not scrolled. That's where we want it, but we want it to stay at the bottom
        // even if the block is overflowing and scrolled, and bottom:0 doesn't keep it
        // there; the button scrolls with the other content. We can't use postion: sticky,
        // because this may be inside other scrolling elements. So we do this hack.
        $("#formatButton").css({
            bottom: -$(element).scrollTop()
        });
    }

    public AttachToBox(targetBox: HTMLElement) {
        // This method is called when the window gets focus. This may be before CkEditor has finished loading.
        // Somewhere in the course of loading, it detects editable divs that are empty except for our gear icon.
        // It decides to insert some content...typically <p><br></p>, and in doing so, replaces the gear icon div.
        // Attempts to suppress this with  config.fillEmptyBlocks, config.protectedSource,
        // config.allowedContent, and data-cke-survive did not work.
        // The only solution we have found is to postpone adding the gear icon until CkEditor has done
        // its nefarious work. The following block achieves this.
        // Enhance: this logic is roughly duplicated in toolbox.ts restoreToolboxSettingsWhenCkEditorReady.
        // There may be some way to refactor it into a common place, but I don't know where.
        const editorInstances = (<any>window).CKEDITOR.instances;
        for (let i = 1; ; i++) {
            const instance = editorInstances["editor" + i];
            if (instance == null) {
                if (i === 0) {
                    // no instance at all...if one is later created, get us invoked.
                    (<any>window).CKEDITOR.on("instanceReady", e =>
                        this.AttachToBox(targetBox)
                    );
                    return;
                }
                break; // if we get here all instances are ready
            }
            if (!instance.instanceReady) {
                instance.on("instanceReady", e => this.AttachToBox(targetBox));
                return;
            }
        }

        let styleName = StyleEditor.GetStyleNameForElement(targetBox);
        if (!styleName) {
            return;
        }
        // I'm assuming here that since we're dealing with a local server, we'll get a result long before
        // the user could actually modify a style and thus need the information.
        // More dangerous is using it in getCharTabDescription. But as that is launched by a later
        // async request, I think it should be OK.

        BloomApi.get("authorMode", result => {
            this.authorMode = result.data === true;
        });
        this.xmatterMode = IsPageXMatter($(targetBox));

        if (this._previousBox != null) {
            StyleEditor.CleanupElement(this._previousBox);
        }
        this._previousBox = targetBox;

        $("#format-toolbar").remove(); // in case there's still one somewhere else

        // put the format button in the editable text box itself, so that it's always in the right place.
        // unfortunately it will be subject to deletion because this is an editable box. But we can mark it as uneditable, so that
        // the user won't see resize and drag controls when they click on it
        $(targetBox).append(
            '<div id="formatButton" contenteditable="false" class="bloom-ui"><img  contenteditable="false" src="' +
                this._supportFilesRoot +
                '/img/cogGrey.svg"></div>'
        );

        //make the button stay at the bottom if we overflow and thus scroll
        //review: It's not clear to me that this is actually working (JH 3/19/2016)
        $(targetBox).on("scroll", e => {
            this.AdjustFormatButton(e.target);
        });

        // And in case we are starting out on a centerVertically page we might need to adjust it now
        this.AdjustFormatButton(targetBox);

        const formatButton = $("#formatButton");
        /* we removed this for BL-799, plus it was always getting in the way, once the format popup was opened
        const txt = theOneLocalizationManager.getText('EditTab.FormatDialogTip', 'Adjust formatting for style');
        editor.AddQtipToElement(formatButton, txt, 1500);
        */

        // BL-2476: Readers made from BloomPacks should have the formatting dialog disabled
        const suppress = $(document).find('meta[name="lockFormatting"]');
        const noFormatChange =
            suppress.length > 0 &&
            suppress.attr("content").toLowerCase() === "true";
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

        // It is not reliable to attach the click handler directly, as in  $(#formatButton).click(...)
        // I don't know why it doesn't work because even when it fails $(#formatButton).length is 1, so it seems to be
        // finding the right element. But some of the time it doesn't work. See BL-2701. This rather awkard
        // approach is the recommended way to make events fire for dynamically added elements.
        // The .off prevents adding multiple event handlers as the parent box gains focus repeatedly.
        // The namespace (".formatButton") in the event name prevents off from interfering with other click handlers.
        $(targetBox).off("click.formatButton");
        $(targetBox).on("click.formatButton", "#formatButton", () => {
            // Using axios directly because BloomApi does not support combining multiple promises with .all
            BloomApi.wrapAxios(
                axios
                    .all([
                        axios.get("/bloom/availableFontNames"),
                        axios.get(
                            "/bloom/bookEdit/StyleEditor/StyleEditor.html"
                        )
                    ])
                    .then(results => {
                        const fonts = results[0].data["fonts"];
                        const html = results[1].data;

                        this.boxBeingEdited = targetBox;
                        styleName = StyleEditor.GetBaseStyleNameForElement(
                            targetBox
                        );
                        const current = this.getFormatValues();

                        //alert('font: ' + fontName + ' size: ' + sizeString + ' height: ' + lineHeight + ' space: ' + wordSpacing);
                        // Enhance: lineHeight may well be something like 35px; what should we select initially?

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
                            if (!this.authorMode || this.xmatterMode) {
                                $("#style-page").remove();
                            }

                            const visibleTabs = $(".tab-page:visible");
                            if (visibleTabs.length === 1) {
                                // When there is only one tab, we want to hide the tab itself.
                                $(visibleTabs[0])
                                    .find("h2.tab")
                                    .remove();
                            }

                            this.setupSelectControls(
                                fonts,
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
                        if (!noFormatChange) {
                            this.getCharTabDescription();
                            this.getParagraphTabDescription();

                            $("#font-select").change(() => {
                                this.changeFont();
                            });
                            this.AddQtipToElement(
                                $("#font-select"),
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
                            if (this.authorMode && !this.xmatterMode) {
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
                        $("html").on("click.toolbar", event => {
                            if (
                                event.target !== toolbar.get(0) &&
                                toolbar.has(event.target).length === 0 &&
                                $(event.target).parent() !== toolbar &&
                                toolbar.has(event.target).length === 0 &&
                                toolbar.is(":visible")
                            ) {
                                toolbar.remove();
                                event.stopPropagation();
                                event.preventDefault();
                            }
                        });
                        toolbar.on("click.toolbar", event => {
                            // this stops an event inside the dialog from propagating to the html element, which would close the dialog
                            event.stopPropagation();
                        });
                    })
            );
        });
    }

    public setupSelectControls(
        fonts: string[],
        current: IFormattingValues,
        styleName: string
    ) {
        this.populateSelect(
            fonts,
            current.fontName,
            "font-select",
            true,
            false,
            25
        );
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
            "position-leading",
            "position-center",
            "indent-none",
            "indent-indented",
            "indent-hanging"
        ];
    }

    public selectButtons(current) {
        this.selectButton("bold", current.bold);
        this.selectButton("italic", current.italic);
        this.selectButton("underline", current.underline);
        this.selectButton("position-center", current.center);
        this.selectButton("position-leading", !current.center);
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
    public getCharTabDescription() {
        const styleName = StyleEditor.GetBaseStyleNameForElement(
            this.boxBeingEdited
        );
        // BL-2386 This one should NOT be language-dependent; only style dependent
        // BL-5616 This also applies if the textbox's default language is '*',
        // like it is for an Arithmetic Equation.
        const iso = $(this.boxBeingEdited).attr("lang");
        if (this.shouldSetDefaultRule() || iso === "*") {
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
        let lang = theOneLocalizationManager.getLanguageName(iso);
        if (!lang) {
            lang = iso;
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
        this.changeFont();
        this.changeSize();
        this.changeLineheight();
        this.changeWordSpace();
        this.changeIndent();
        this.changeBold();
        this.changeItalic();
        this.changeUnderline();
        this.changeParaSpacing();
        this.changePosition();
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
                text = parseFloat(text).toLocaleString();
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

    public changeFont() {
        if (this.ignoreControlChanges) {
            return;
        }
        const rule = this.getStyleRule(false);
        if (rule != null) {
            const font = $("#font-select").val();
            rule.style.setProperty("font-family", font, "important");
            this.cleanupAfterStyleChange();
        }
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
        const defLang = (<any>GetSettings()).languageForNewTextBoxes;
        if ($(target).attr("lang") !== defLang) {
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
        // Always set the value in the language-specific rule
        let rule = this.getStyleRule(false);
        if (rule != null)
            rule.style.setProperty(
                "font-size",
                sizeString + units,
                "important"
            );
        if (this.shouldSetDefaultRule()) {
            rule = this.getStyleRule(true);
            if (rule != null)
                rule.style.setProperty(
                    "font-size",
                    sizeString + units,
                    "important"
                );
        }
        this.cleanupAfterStyleChange();
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

    public changePosition() {
        if (this.ignoreControlChanges) {
            return;
        }
        const rule = this.getStyleRule(true);
        let position = "initial";
        if ($("#position-center").hasClass("selectedIcon")) {
            position = "center";
        }
        if (rule != null) {
            rule.style.setProperty("text-align", position, "important");
            this.cleanupAfterStyleChange();
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
        const style = $("#styleSelect").val();
        $("#style-select-input").val(""); // we've chosen a style from the list, so we aren't creating a new one.
        StyleEditor.SetStyleNameForElement(
            this.boxBeingEdited,
            style + "-style"
        );
        const predefined = this.getPredefinedStyle(style + "-style");
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
        this.UpdateControlsToReflectAppliedStyle();
    }

    public UpdateControlsToReflectAppliedStyle() {
        const current = this.getFormatValues();
        this.ignoreControlChanges = true;

        this.setValueAndUpdateSelect2Control("font-select", current.fontName);
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
        this.getCharTabDescription();
        this.getParagraphTabDescription();
    }

    // Remove any additions we made to the element for the purpose of UI alone
    public static CleanupElement(element) {
        $(element)
            .find(".bloom-ui")
            .each(function() {
                $(this).remove();
            });
        //stop watching the scrolling event we used to keep the formatButton at the bottom
        $(element).off("scroll");
    }
}
