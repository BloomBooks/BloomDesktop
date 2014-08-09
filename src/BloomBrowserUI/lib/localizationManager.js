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
 */
function LocalizationManager() {
    this.dictionary = {};
}

/**
 * Retrieves localized strings from the server
 * Used in Bloom 2.1
 * @param {Object} [keyValuePairs] Optional. Each property name (i.e. keyValuePairs.keys) is a string id, and the
 * property value is the default value/english text. If keyValuePairs is omitted, all dictionary entries will be
 * returned, otherwise only the requested entries will be returned.
 */
LocalizationManager.prototype.loadStrings = function (keyValuePairs) {

    // NOTE: This function is not used in Bloom 2.0, but will be used in Bloom 2.1
    // NOTE: The file "i18n.html" does not exist on the hard drive. The EnhancedImageServer class intercepts this request and handles it appropriately.
    // TODO: Implement this functionality in the C# EnhancedImageServer and RequestInfo classes for Bloom 2.1.

    var ajaxSettings = {type: 'POST', url: '/bloom/i18n.html'};
    if (keyValuePairs) ajaxSettings.data = keyValuePairs;

    $.ajax(ajaxSettings)
        .done(function (data) {
            localizationManager.dictionary = $.extend(localizationManager.dictionary, JSON.parse(data));
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
}

/**
 * Gets translated text.
 * Additional parameters after the englishText are treated as arguments for SimpleDotNetFormat.
 * @param {String} stringId
 * @param {String} [englishText]
 * @returns {String}
 */
LocalizationManager.prototype.getText = function (stringId, englishText) {

    // TODO: Evaluate if this dependence on GetDictionary() should be removed in Bloom 2.1
    if (Object.keys(this.dictionary).length == 0)
        this.loadStringsFromObject(GetDictionary());

    // check if englishText is missing
    englishText = englishText || stringId;

    // get the translation
    var text = this.dictionary[stringId];

    // In the XMatter, there are string keys that are not exact matches for what is in the *.tmx localization files. For
    // example, the *.tmx files contain the key "FrontMatter.Factory.Book title in {lang}" but where it is used in the
    // jade and html files, the key is simply "Book title in {lang}." If there is not an exact match found for stringId,
    // this block looks for keys in the dictionary that follow the pattern "*.stringId".

    //TODO: Evaluate if this should be moved to the server
    if (!text) {
        var keys = Object.keys(this.dictionary);
        var regex = new RegExp('\\.(' + stringId + ')$');

        for (var i = 0; i < keys.length; i++) {
            if (regex.test(keys[i])) {
                text = this.dictionary[keys[i]];
                break;
            }
        }
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
