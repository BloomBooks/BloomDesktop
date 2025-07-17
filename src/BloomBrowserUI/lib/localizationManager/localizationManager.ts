/// <reference path="../misc-types.d.ts" />
///<reference path="../../typings/bundledFromTSC.d.ts"/>
import axios, { AxiosResponse } from "axios";
import { getBloomApiPrefix } from "../../utils/bloomApi";

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
 *   3. C# loads the resulting html into a browser control.
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
     * The strings are localized in the UI language
     * @param {Object} [keyValuePairs] Optional. Each property name (i.e. keyValuePairs.keys) is a string id, and the
     * property value is the default value/english text. If keyValuePairs is omitted, all dictionary entries will be
     * returned, otherwise only the requested entries will be returned.
     * @param [elementsToLocalize]
     * @param {Function} [callbackDone] Optional function to call when done.
     */
    public loadStrings(keyValuePairs, elementsToLocalize, callbackDone): void {
        // NOTE: This function is not used in Bloom 2.0, but will be used in Bloom 2.1

        const ajaxSettings: JQueryAjaxSettings = <JQueryAjaxSettings>{
            type: "POST",
            url: "/bloom/api/i18n/loadStrings"
        };
        if (keyValuePairs) ajaxSettings["data"] = keyValuePairs;

        $.ajax(ajaxSettings).done(data => {
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

    public loadStringsPromise(
        keyValuePairs,
        elementsToLocalize
    ): Promise<void> {
        return new Promise<void>((resolve, reject) => {
            this.loadStrings(keyValuePairs, elementsToLocalize, resolve);
        });
    }

    public getCurrentUILocale(): string {
        // This is set in C# code by setting the "intl.accept_languages" preference.
        return navigator.language;
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
        let text = this.dictionary[stringId];
        if (!text) {
            text = this.dictionary[stringId.replace("&", "&amp;")];
        }

        // try to get from L10NSharp
        if (!text) {
            const ajaxSettings: JQueryAjaxSettings = <JQueryAjaxSettings>{
                type: "POST",
                url: "/bloom/api/i18n/loadStrings"
            };
            const pair = {};
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
            text = this.simpleFormat(text, args);
        }

        return text;
    }

    /* Returns a promise to get the translation.
     *
     * @param {String} langId : can be an BCP 47 code or one of these constants: UI, V, N1, N2
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
        comment: string | undefined,
        ...args
    ): JQueryPromise<any> {
        return this.asyncGetTextInLangCommon(
            id,
            englishText,
            "UI",
            comment,
            true,
            false,
            false,
            args
        );
    }
    public asyncGetTextAndSuccessInfo(
        id: string,
        englishText: string,
        comment: string | undefined,
        temporarilyDisableI18nWarning: boolean,
        ...args
    ): JQueryPromise<any> {
        return this.asyncGetTextInLangCommon(
            id,
            englishText,
            "UI",
            comment,
            true,
            true,
            temporarilyDisableI18nWarning,
            args
        );
    }

    // This method tries to get the translation in the given language. If we don't have it,
    // we try other languages: L1, N1, N2, current UI language, any fallback langauges
    // currently configured in L10NSharp, and finally English.
    // There is deliberately no English default; we will retrieve whatever English is in the
    // XLF for this ID, if there is no better alternative available (or if langId is "en").
    // The result includes both the text and the identifier of the language that was found.
    public async asyncGetTextInLangWithLangFound(
        id: string,
        langId
    ): Promise<{ text: string; langFound: string }> {
        return axios
            .get(`${getBloomApiPrefix()}i18n/getStringInLang`, {
                params: {
                    key: id,
                    langId: langId
                }
            })
            .then(
                response => response.data as { text: string; langFound: string }
            );
    }

    public asyncGetTextInLangCommon(
        id: string,
        englishText: string,
        langId: string,
        comment: string | undefined,
        englishDefault: boolean,
        includeSuccessInfo: boolean,
        temporarilyDisableI18nWarning: boolean,
        args
    ): JQueryPromise<any> {
        // We already get a promise from the async call, and could just return that.
        // But we want to first massage the data we get back from the ajax call, before we re - "send" the result along
        //to the caller. So, we do that by making our *own* deferred object, and "resolve" it with the massaged value.
        const deferred = $.Deferred();

        const bloomApiPrefix: string = getBloomApiPrefix();

        //when the async call comes back, we massage the text
        // Using axios directly because we have specific catch behavior
        axios
            .get(`${bloomApiPrefix}/i18n/translate`, {
                params: {
                    key: id,
                    englishText: englishText,
                    langId: langId,
                    comment: comment || "",
                    dontWarnIfMissing: temporarilyDisableI18nWarning
                }
            })
            .then(response => {
                let text = HtmlDecode(response.data.text);
                // is this a C#-style string.format style request?
                if (args.length > 0) {
                    text = this.simpleFormat(text, args);
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
                        text = this.simpleFormat(text, args);
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
        const key = element.dataset["i18n"];
        if (!key) return;

        const elem = $(element);
        const text = this.getText(key, elem.html());

        // Hmm... theoretically an XSS vulnerability,
        // but the translations need to be approved, I would think any XSS injections should be obviously wrong and get rejected.
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
        const args = Array.prototype.slice.call(arguments);
        args[1] = null; //we're passing null into the gettext englishText arg
        // this awkward, fragile method call sends along the 2 fixed arguments
        // to the method, plus any extra arguments we might have been called with,
        // as parameters for a  c#-style template string
        const translated = this.getText.apply(this, args);

        // stick in the language
        return this.insertLangIntoHint(translated, targetElement);
    }

    // Hints sometimes have a {lang} tag in the text that needs to be substituted.
    public insertLangIntoHint(whatToSay, targetElement: any) {
        let translated = whatToSay;
        if (translated.indexOf("{lang}") != -1) {
            //This is the preferred approach, but it's not working yet.
            //var languageName = localizationManager.dictionary[$(targetElement).attr('lang')];
            const languageName = GetInlineDictionary()[
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

    public getLanguageName(langTag): string {
        return this.getText(langTag);
    }

    /**
     * Return a formatted string.
     * Replaces {0}, {1} ... {n} with the corresponding elements of the args array.
     * Replaces %0, %1...%n with the corresponding elements of the args array
     * (Thus, callers can use either C or DotNet param substitution)
     * @param {String} format
     * @param {String[]} args
     * @returns {String}
     */
    public simpleFormat(format: string, args: (string | undefined)[]): string {
        // The match functions here are tricky. The first argument is the whole match, e.g., {0}, which is no use
        // and is ignored. The second argument is the first capture group, thus typically a string that looks
        // like a number, e.g., "0". The code then takes advantage of Javascript's ability to use a number-like
        // string in place of a number to index an array. Telling typescript that the second function argument
        // is a number is a trick. It's really a string, but since it's the result of matching \d+, we can be
        // sure it will work as a number.
        const dotNetOutput = format.replace(
            /{(\d+)}/g,
            (match: string, index: number) => {
                return args[index] ?? match;
            }
        );
        return dotNetOutput.replace(
            /%(\d+)/g,
            (match: string, index: number) => {
                return args[index] ?? match;
            }
        );
    }

    /**
     * Convert simple Markdown markup into HTML.  Only **, *, and the simplest []() are
     * handled for now.
     * @param {String} text
     * @returns {String}
     */
    public processSimpleMarkdown(text: string): string {
        if (text.indexOf("*") < 0 && text.indexOf("[") < 0) return text;
        const reStrong = /(^|[^*])\*\*([^*]+)\*\*([^*]|$)/g;
        let newstr = text.replace(reStrong, "$1<strong>$2</strong>$3");
        const reEm = /(^|[^*])\*([^*]+)\*([^*]|$)/g;
        newstr = newstr.replace(reEm, "$1<em>$2</em>$3");
        const reA = /\[([^\]]*)\]\(([^)]*)\)/g;
        newstr = newstr.replace(reA, '<a href="$2">$1</a>');
        return newstr;
    }

    /**
     * Find the translation for the given id (and presumably the text) in the UI
     * language.  This differs from asyncGetText() largely in returning a native
     * AxiosResponse promise instead of a JQuery promise.  This allows it to be
     * used inside an axios.all() collection of parallel calls.
     *
     * @param {String} id
     * @param {String} text
     * @param {String|undefined} comment (optional)
     * @returns Promise<AxiosResponse<any>> for translated text
     */
    public getTextInUiLanguageAsync(
        id: string,
        text: string,
        comment?: string
    ): Promise<AxiosResponse<any>> {
        return axios.get("/bloom/api/i18n/translate", {
            params: {
                key: id,
                englishText: text,
                langId: "UI",
                comment: comment,
                dontWarnIfMissing: true
            }
        });
    }
}

/**
 * Returns a string where html entities have been converted to normal text
 * @param {String} text
 */
function HtmlDecode(text): string {
    if (text.indexOf("&") < 0) {
        // no need to decode anything!
        return text;
    }
    const div = document.createElement("div");
    div.innerHTML = text;
    if (text.indexOf("<") < 0) {
        // cheap decoding using built-in functionality
        return div.firstChild!.nodeValue!;
    }
    let retval = "";
    for (let i = 0; i < div.childNodes.length; ++i) {
        // (childNodes.forEach doesn't exist until Firefox 50.)
        const node = div.childNodes[i] as HTMLElement;
        if (node) {
            if (node.firstChild) {
                retval =
                    retval +
                    extractStartTag(node.outerHTML) +
                    HtmlDecode(node.innerHTML) +
                    "</" +
                    node.localName +
                    ">";
            } else {
                // empty element, no text to have any encoded characters
                retval = retval + node.outerHTML;
            }
        } else {
            const nodeValue = div.childNodes[i].nodeValue;
            if (nodeValue) {
                retval = retval + div.childNodes[i].nodeValue;
            } else {
                retval = retval + "[UNKNOWN VALUE]";
            }
        }
    }
    return retval;
}

function extractStartTag(text: string): string {
    return text.substring(0, text.indexOf(">") + 1);
}

const theOneLocalizationManager: LocalizationManager = new LocalizationManager();
export default theOneLocalizationManager;
