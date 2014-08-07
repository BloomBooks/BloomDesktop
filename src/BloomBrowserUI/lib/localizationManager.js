/**
 * L10NSharp LocalizationManager for javascript
 */
function LocalizationManager() {
    this.dictionary = {};
}

/**
 * Retrieves localized strings from the server
 * Used in Bloom 2.1
 * @param {Object} keyValuePairs
 */
LocalizationManager.prototype.loadStrings = function (keyValuePairs) {

    $.ajax({
        type: 'POST',
        url: '/bloom/i18n.html',
        data: keyValuePairs
    })
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
    this.dictionary = keyValuePairObject;
}

/**
 * Gets translated text.
 * Additional parameters after the englishText are treated as arguments for vsprintf.
 * @param {String} stringId
 * @param {String} [englishText]
 * @returns {String}
 */
LocalizationManager.prototype.getText = function (stringId, englishText) {

    if (Object.keys(this.dictionary).length == 0)
        this.loadStringsFromObject(GetDictionary());

    // check if englishText is missing
    englishText = englishText || stringId;

    // get the translation
    var text = this.dictionary[stringId];

    // look for "FrontMatter.Factory.Some text key"
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
        text = vsprintf(text, args.slice(2));
    }

    return text;
};

/**
 * Sets the translated text of elem.
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
function vsprintf(format, args) {

    for (var i = 0; i < args.length; i++) {
        var regex = new RegExp('\\{' + i + '\\}' , 'g');
        format = format.replace(regex, args[i]);
    }
    return format;
}
