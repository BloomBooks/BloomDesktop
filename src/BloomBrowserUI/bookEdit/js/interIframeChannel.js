/// <reference path="../../lib/jquery.d.ts" />
/**
 * Class to hold information passed between iframes
 * @constructor
 */
var interIframeChannel = (function () {
    function interIframeChannel() {
        this.localizationManagerDictionary = {};
        this.readableFileExtensions = ['txt', 'js', 'json'];
    }
    /**
     * Opens the application help topic
     * @param topic
     * @returns {boolean} Returns false to prevent navigation if link clicked.
     */
    interIframeChannel.prototype.help = function (topic) {
        this.simpleAjaxNoCallback('/bloom/help', topic);
        return false;
    };
    /**
     * Retrieve data from localhost
     * @param {String} url The URL to request
     * @param {Function} callback Function to call when the ajax request returns
     * @param [dataValue] Passed in the query string under the "data" key.
     * NOTE: If dataValue is a string, it must NOT be URI encoded.
     */
    interIframeChannel.prototype.simpleAjaxGet = function (url, callback, dataValue) {
        // We are calling encodeURIComponent() if dataValue is a string.
        // NOTE: We are encoding every string, so the caller should NOT encode the string.
        if (typeof dataValue === 'string')
            dataValue = encodeURIComponent(dataValue);
        var ajaxSettings = { type: 'GET', url: url };
        if (dataValue)
            ajaxSettings['data'] = { data: dataValue };
        $.ajax(ajaxSettings).done(function (data) {
            callback(data);
        });
    };
    /**
    * Gets the data and returns a promise
    */
    interIframeChannel.prototype.asyncGet = function (url, dataValue) {
        // We are calling encodeURIComponent() if dataValue is a string.
        // NOTE: We are encoding every string, so the caller should NOT encode the string.
        if (typeof dataValue === 'string')
            dataValue = encodeURIComponent(dataValue);
        var ajaxSettings = { type: 'GET', url: url };
        if (dataValue)
            ajaxSettings['data'] = dataValue;
        return $.ajax(ajaxSettings).promise();
    };
    /*
     * This will earn you the following message in the console:
     *  "Synchronous XMLHttpRequest on the main thread is deprecated because of its detrimental effects to the end user's experience. For more help http://xhr.spec.whatwg.org/"
     */
    interIframeChannel.prototype.getValueSynchronously = function (url, parameters) {
        var ajaxSettings = { type: 'GET', url: url, async: false };
        if (parameters)
            ajaxSettings['data'] = parameters;
        return $.ajax(ajaxSettings).responseText;
    };
    /**
     * Retrieve data from localhost
     * @param {String} url The URL to request
     * @param {Function} callback Function to call when the ajax request returns
     * @param {Object} callbackParam Parameter passed to the callback function
     * @param [dataValue] Passed in the query string under the "data" key
     * NOTE: If dataValue is a string, it must NOT be URI encoded.
     */
    interIframeChannel.prototype.simpleAjaxGetWithCallbackParam = function (url, callback, callbackParam, dataValue) {
        // We are calling encodeURIComponent() if dataValue is a string.
        // NOTE: We are encoding every string, so the caller should NOT encode the string.
        if (typeof dataValue === 'string')
            dataValue = encodeURIComponent(dataValue);
        var ajaxSettings = { type: 'GET', url: url };
        if (dataValue)
            ajaxSettings['data'] = { data: dataValue };
        $.ajax(ajaxSettings).done(function (data) {
            callback(data, callbackParam);
        });
    };
    /**
     * Retrieve data from localhost
     * @param {String} url The URL to request
     * @param {Function} callback Function to call when the ajax request returns
     * @param {String} [dataValue] Passed in the post under the "data" key
     */
    interIframeChannel.prototype.simpleAjaxPost = function (url, callback, dataValue) {
        var ajaxSettings = { type: 'POST', url: url };
        if (dataValue)
            ajaxSettings['data'] = { data: dataValue };
        $.ajax(ajaxSettings).done(function (data) {
            callback(data);
        });
    };
    /**
     * Sends data to localhost
     * @param {String} url The URL to request
     * @param {String} [dataValue] Passed in the post under the "data" key
     */
    interIframeChannel.prototype.simpleAjaxNoCallback = function (url, dataValue) {
        var ajaxSettings = { type: 'POST', url: url };
        if (dataValue)
            ajaxSettings['data'] = { data: dataValue };
        $.ajax(ajaxSettings);
    };
    interIframeChannel.prototype.getPageWindow = function () {
        return document.getElementById('page').contentWindow;
    };
    return interIframeChannel;
})();
//# sourceMappingURL=interIframeChannel.js.map