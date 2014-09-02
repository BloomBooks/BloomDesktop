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
 *      into a script tag that defines the JS function GetDictionary().
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
function LocalizationManager() {

    this.dictionary = {};

    // get the dictionary from the main document, if present
    if (typeof localizationManagerDictionary === 'object')
        this.dictionary = localizationManagerDictionary;
    else if (typeof window.parent.localizationManagerDictionary === 'object')
        this.dictionary = window.parent.localizationManagerDictionary;
}

/**
 * Retrieves localized strings from the server
 * Used in Bloom 2.1
 * @param {Object} [keyValuePairs] Optional. Each property name (i.e. keyValuePairs.keys) is a string id, and the
 * property value is the default value/english text. If keyValuePairs is omitted, all dictionary entries will be
 * returned, otherwise only the requested entries will be returned.
 * @param [elementsToLocalize]
 */
LocalizationManager.prototype.loadStrings = function (keyValuePairs, elementsToLocalize) {

    // NOTE: This function is not used in Bloom 2.0, but will be used in Bloom 2.1

    var ajaxSettings = {type: 'POST', url: '/bloom/i18n/loadStrings'};
    if (keyValuePairs) ajaxSettings.data = keyValuePairs;

    $.ajax(ajaxSettings)
        .done(function (data) {
            localizationManager.dictionary = $.extend(localizationManager.dictionary, data);
            if (elementsToLocalize) {
                $(elementsToLocalize).each(function() {
                    localizationManager.setElementText(this);
                });
            }
        });
};

/**
 * Set dictionary values from an object.
 * Used in Bloom 2.0
 * @param {Object} keyValuePairObject
 */
LocalizationManager.prototype.loadStringsFromObject = function (keyValuePairObject) {
    // TODO: Evaluate if this function is needed in Bloom 2.1
    this.dictionary = keyValuePairObject;
};

/**
 * Gets translated text.
 * Additional parameters after the englishText are treated as arguments for SimpleDotNetFormat.
 * @param {String} stringId
 * @param {String} [englishText]
 * @returns {String}
 */
LocalizationManager.prototype.getText = function (stringId, englishText) {

    if (typeof GetDictionary === 'function') {
        if (Object.keys(this.dictionary).length == 0)
            this.loadStringsFromObject(GetDictionary());
    }

    // check if englishText is missing
    englishText = englishText || stringId;

    // get the translation
    var text = this.dictionary[stringId];
    if (!text) {
        text = this.dictionary[stringId.replace('&','&amp;')];
    }
    // use default if necessary
    if (!text) text = englishText;

    // is this a string.format style request?
    var args = jQuery.makeArray(arguments);
    if (args.length > 2) {

        // Do the formatting.
        // Remove the first 2 elements, which are the stringId and englishText.
        text = SimpleDotNetFormat(text, args.slice(2));
    }

    return text;
};

/**
 * Sets the translated text of element.
 * @param element
 */
LocalizationManager.prototype.setElementText = function (element) {

    var key = element.dataset.i18n;
    if (!key) return;

    var elem = $(element);
    var text = this.getText(key, elem.text());

    if (text) elem.text(text);
};

/**
 * Hints sometimes have a {lang} tag in the text that needs to be substituted.
 * Replaces {0}, {1} ... {n} with the corresponding elements of the args array.
 * @param {String} whatToSay
 * @param {element} targetElement
 * @returns {String}
 */
LocalizationManager.prototype.getLocalizedHint = function (whatToSay, targetElement) {
    var args = Array.prototype.slice.call(arguments);
    args[1] = null; //we're passing null into the gettext englishText arg
    // this awkward, fragile method call sends along the 2 fixed arguments
    // to the method, plus any extra arguments we might have been called with,
    // as parameters for a  c#-style template string
    var translated = this.getText.apply(this, args);

    // stick in the language
    if (translated.indexOf('{lang}') != -1)
        translated = translated.replace("{lang}", localizationManager.dictionary[$(targetElement).attr('lang')]);

    return translated;
};

LocalizationManager.prototype.getVernacularLang = function () {
    return this.getText('vernacularLang');
};

LocalizationManager.prototype.getLanguageName = function(iso) {
    return this.getText(iso);
}

var localizationManager = new LocalizationManager();

/**
 * Return a formatted string.
 * Replaces {0}, {1} ... {n} with the corresponding elements of the args array.
 * @param {String} format
 * @param {String[]} args
 * @returns {String}
 */
function SimpleDotNetFormat(format, args) {

    for (var i = 0; i < args.length; i++) {
        var regex = new RegExp('\\{' + i + '\\}', 'g');
        format = format.replace(regex, args[i]);
    }
    return format;
}
