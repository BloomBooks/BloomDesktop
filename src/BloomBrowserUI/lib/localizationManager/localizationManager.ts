/// <reference path="../misc-types.d.ts" />
///<reference path="../../typings/bundledFromTSC.d.ts"/>
import axios from "axios";

/**
 * L10NSharp LocalizationManager for javascript.
 *
 * For Bloom 2.0: Since the javascript files are being loaded using file:// urls, ajax is not possible
 * so the dictionary is loaded into the DOM using the C# class RuntimeInformationInjector, and into the
 * localizationManager using localizationManager.loadStringsFromObject().
 *
 * For Bloom 2.1 and following: The javascript files are being loaded using http://localhost, so it is
 * now possible to load the dictionary using ajax through the C# EnhancedImageServer class. The dictionary
 * is retrieved and loaded by calling localizationManager.loadStrings().
 *
 * Bloom 2.0 String Localization steps:
 *   1. C# RuntimeInformationInjector.AddUIDictionaryToDom() is called when loading an page into the editor, which
 *      retrieves all the localized strings that may be needed by the editing page, using L10NSharp, and loads them
 *      into a script tag that defines the JS function GetInlineDictionary().
 *   2. C# RuntimeInformationInjector.AddLocalizationTriggerToDom() is called, adding a script tag to the page that
 *      instructs the browser control to gather all elements that have a "data-i18n" attribute once the document has
 *      finished loading, and run the jQuery.fn.localize() function.
 *   3. C# loads the resulting html into a GeckoFx browser control.
 *   4. The browser executes the trigger script once the document has finished loading.
 *   5. The JS function jQuery.fn.localize() loops through each element with a "data-i18n" attribute and passes it to
 *      localizationManager.setElementText().
 *   6. The JS function localizationManager.setElementText() reads the stringId from the "data-i18n" attribute and uses
 *      it to search for the localized text in the dictionary created in step 1.
 *   7. There is a separate function, localizationManager.getLocalizedHint(), for localizing the text of hint bubbles
 *      because sometimes there is a {lang} tag in the hint text that needs to be substituted.
 *   8. Strings that are generated dynamically by JS can also be localized by the localizationManager. An example of
 *      this is found in StyleEditor.GetToolTip(), where the tip for the font size changer is built.
 */
declare function GetInlineDictionary(): any; //c# injects this

export class LocalizationManager {
    public dictionary: any;
    private inlineDictionaryLoaded: boolean = false;

    constructor() {
        this.dictionary = {};
    }

    /**
     * Retrieves localized strings from the server
     * Used in Bloom 2.1
     * @param {Object} [keyValuePairs] Optional. Each property name (i.e. keyValuePairs.keys) is a string id, and the
     * property value is the default value/english text. If keyValuePairs is omitted, all dictionary entries will be
     * returned, otherwise only the requested entries will be returned.
     * @param [elementsToLocalize]
     * @param {Function} [callbackDone] Optional function to call when done.
     */
    public loadStrings(keyValuePairs, elementsToLocalize, callbackDone): void {
        // NOTE: This function is not used in Bloom 2.0, but will be used in Bloom 2.1

        var ajaxSettings: JQueryAjaxSettings = <JQueryAjaxSettings>{
            type: "POST",
            url: "/bloom/api/i18n/loadStrings"
        };
        if (keyValuePairs) ajaxSettings["data"] = keyValuePairs;

        $.ajax(ajaxSettings).done(function(data) {
            theOneLocalizationManager.dictionary = $.extend(
                theOneLocalizationManager.dictionary,
                data
            );

            // if callback is passes without a list of elements to localize...
            if (typeof elementsToLocalize === "function") {
                elementsToLocalize();
            } else if (elementsToLocalize) {
                $(elementsToLocalize).each(function() {
                    theOneLocalizationManager.setElementText(this);
                });
                if (typeof callbackDone === "function") callbackDone();
            } else if (typeof callbackDone === "function") callbackDone();
        });
    }

    /**
     * Set dictionary values from an object.
     * Used in Bloom 2.0
     * @param {Object} keyValuePairObject
     */
    public loadStringsFromObject(keyValuePairObject): void {
        // TODO: Evaluate if this function is needed in Bloom 2.1
        this.dictionary = keyValuePairObject;
    }

    /**
     * WARNING! This gets the translated text only if it is already loaded. Otherwise it just gives English back and will give translation next time.
     * Instead, use asyncGetTextInLang().
     *
     * Additional parameters after the englishText are treated as arguments for simpleDotNetFormat.
     * @param {String} stringId
     * @param {String} [englishText]
     * @param [args]
     * @returns {String}
     */
    public getText(stringId: string, englishText?: string, ...args): string {
        if (typeof stringId === "undefined") {
            try {
                throw new Error(
                    "localizationManager.getText() stringid was undefined"
                );
            } catch (e) {
                throw e.message + e.stack;
            }
        }
        if (
            !this.inlineDictionaryLoaded &&
            typeof GetInlineDictionary === "function"
        ) {
            if (Object.keys(this.dictionary).length == 0) {
                this.inlineDictionaryLoaded = true;
                $.extend(
                    theOneLocalizationManager.dictionary,
                    GetInlineDictionary()
                );
            }
        }

        // check if englishText is missing
        englishText = englishText || stringId;

        // get the translation
        var text = this.dictionary[stringId];
        if (!text) {
            text = this.dictionary[stringId.replace("&", "&amp;")];
        }

        // try to get from L10NSharp
        if (!text) {
            var ajaxSettings: JQueryAjaxSettings = <JQueryAjaxSettings>{
                type: "POST",
                url: "/bloom/api/i18n/loadStrings"
            };
            var pair = {};
            pair[stringId] = englishText;
            ajaxSettings["data"] = pair;

            $.ajax(ajaxSettings).done(data => {
                theOneLocalizationManager.dictionary = $.extend(
                    theOneLocalizationManager.dictionary,
                    data
                );
            });

            text = englishText;
        }

        text = HtmlDecode(text);
        // is this a string.format style request?
        if (args.length > 0) {
            // Do the formatting.
            text = this.simpleDotNetFormat(text, args);
        }

        return text;
    }

    /* Returns a promise to get the translation.
     *
     * @param {String} langId : can be an iso 639 code or one of these constants: UI, V, N1, N2
     * @param {String[]} args (optional): can be used as parameters to insert into c#-style parameterized strings
     *  @example
     * asyncGetTextInLang('topics.health','Health', "UI")
     *      .done(translation => {
     *          $(this).text(translation);
     *      })
     *      .fail($(this).text("?Health?"));
     * @example
     * asyncGetTextInLang('topics.health','My name is {0}", "UI", "John")
     *      .done(translation => {
     *          $(this).text(translation);
     *      });
    */
    public asyncGetTextInLang(
        id: string,
        englishText: string,
        langId: string,
        comment: string,
        ...args
    ): JQueryPromise<any> {
        return this.asyncGetTextInLangCommon(
            id,
            englishText,
            langId,
            comment,
            false,
            false,
            args
        );
    }
    /* Returns a promise to get the translation in the current UI language.  If the translation isn't present in the
     * UI language, it returns the english formatted text in the same way getText does.
     *
     * @param {String[]} args (optional): can be used as parameters to insert into c#-style parameterized strings
     *  @example
     * asyncGetText('topics.health','Health')
     *      .done(translation => {
     *          $(this).text(translation);
     *      });
     * @example
     * asyncGetText('topics.health','My name is {0}", "John")
     *      .done(translation => {
     *          $(this).text(translation);
     *      });
    */
    public asyncGetText(
        id: string,
        englishText: string,
        comment: string,
        ...args
    ): JQueryPromise<any> {
        return this.asyncGetTextInLangCommon(
            id,
            englishText,
            "UI",
            comment,
            true,
            false,
            args
        );
    }
    public asyncGetTextAndSuccessInfo(
        id: string,
        englishText: string,
        comment: string,
        ...args
    ): JQueryPromise<any> {
        return this.asyncGetTextInLangCommon(
            id,
            englishText,
            "UI",
            comment,
            true,
            true,
            args
        );
    }

    private asyncGetTextInLangCommon(
        id: string,
        englishText: string,
        langId: string,
        comment: string,
        englishDefault: boolean,
        includeSuccessInfo: boolean,
        args
    ): JQueryPromise<any> {
        // We already get a promise from the async call, and could just return that.
        // But we want to first massage the data we get back from the ajax call, before we re - "send" the result along
        //to the caller. So, we do that by making our *own* deferred object, and "resolve" it with the massaged value.
        var deferred = $.Deferred();

        //when the async call comes back, we massage the text
        // Using axios directly because we have specific catch behavior
        axios
            .get("/bloom/api/i18n/translate", {
                params: {
                    key: id,
                    englishText: englishText,
                    langId: langId,
                    comment: comment
                }
            })
            .then(response => {
                let text = HtmlDecode(response.data.text);
                // is this a C#-style string.format style request?
                if (args.length > 0) {
                    text = this.simpleDotNetFormat(text, args);
                }
                if (!includeSuccessInfo) deferred.resolve(text);
                else {
                    deferred.resolve({
                        text: text,
                        success: response.data.success
                    });
                }
            })

            //TODO: I (JH) could not get this to fire, in a unit test environment, when there was no response.
            .catch(text => {
                if (englishDefault) {
                    text = HtmlDecode(englishText);
                    if (args.length > 0) {
                        text = this.simpleDotNetFormat(text, args);
                    }
                    deferred.resolve(text);
                } else {
                    deferred.fail();
                }
            });
        return deferred.promise();
    }

    public localizeThenSetElementText(
        element: HTMLElement,
        stringId: string,
        englishText: string
    ): void {
        this.asyncGetText(
            stringId,
            englishText,
            $(element).attr("l10nComment")
        ).then(translation => {
            element.innerText = translation;
        });
    }

    /**
     * Sets the translated text of element.
     * @param element
     */
    public setElementText(element): void {
        var key = element.dataset["i18n"];
        if (!key) return;

        var elem = $(element);
        var text = this.getText(key, elem.html());

        if (text) elem.html(text);
    }

    /**
     * Hints sometimes have a {lang} tag in the text that needs to be substituted.
     * Replaces {0}, {1} ... {n} with the corresponding elements of the args array.
     * @param {String} whatToSay
     * @param {element} targetElement
     * @returns {String}
     */
    public getLocalizedHint(whatToSay, targetElement: any): string {
        var args = Array.prototype.slice.call(arguments);
        args[1] = null; //we're passing null into the gettext englishText arg
        // this awkward, fragile method call sends along the 2 fixed arguments
        // to the method, plus any extra arguments we might have been called with,
        // as parameters for a  c#-style template string
        var translated = this.getText.apply(this, args);

        // stick in the language
        return this.insertLangIntoHint(translated, targetElement);
    }

    // Hints sometimes have a {lang} tag in the text that needs to be substituted.
    public insertLangIntoHint(whatToSay, targetElement: any) {
        var translated = whatToSay;
        if (translated.indexOf("{lang}") != -1) {
            //This is the preferred approach, but it's not working yet.
            //var languageName = localizationManager.dictionary[$(targetElement).attr('lang')];
            var languageName = GetInlineDictionary()[
                $(targetElement).attr("lang")
            ];
            if (!languageName) {
                //This can happen, for example, if the user enters {lang} in a hint bubble on a group
                return translated;
            }
            translated = translated.replace("{lang}", languageName);
        }

        return translated;
    }

    public getVernacularLang(): string {
        return this.getText("vernacularLang");
    }

    public getLanguageName(iso): string {
        return this.getText(iso);
    }

    /**
     * Return a formatted string.
     * Replaces {0}, {1} ... {n} with the corresponding elements of the args array.
     * @param {String} format
     * @param {String[]} args
     * @returns {String}
     */
    public simpleDotNetFormat(format: string, args: string[]) {
        return format.replace(/{(\d+)}/g, function(
            match: string,
            index: number
        ) {
            return typeof args[index] !== "undefined" ? args[index] : match;
        });
    }
}

/**
 * Returns a string where html entities have been convert to normal text
 * @param {String} text
 */
function HtmlDecode(text): string {
    if (text === "") {
        //an empty string leads to div.firstChild, below, being null.
        return text;
    }
    var div = document.createElement("div");
    div.innerHTML = text;
    return div.firstChild.nodeValue;
}

var theOneLocalizationManager: LocalizationManager = new LocalizationManager();
export default theOneLocalizationManager;
