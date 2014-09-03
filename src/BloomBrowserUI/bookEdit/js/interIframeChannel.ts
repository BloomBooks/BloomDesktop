/// <reference path="../../lib/jquery.d.ts" />

/**
 * Class to hold information passed between iframes
 * @constructor
 */
class interIframeChannel {

	public localizationManagerDictionary: any;
	public readableFileExtensions: string[];

	constructor() {
		this.localizationManagerDictionary = {};
		this.readableFileExtensions = ['txt', 'js'];
	}

	/**
	 * Opens the application help topic
	 * @param topic
	 * @returns {boolean} Returns false to prevent navigation if link clicked.
	 */
	help(topic: string): boolean {

		this.simpleAjaxNoCallback('/bloom/help', topic);
		return false;
	}

	/**
	 * Retrieve data from localhost
	 * @param {String} url The URL to request
	 * @param {Function} callback Function to call when the ajax request returns
	 * @param {String} [dataValue] Passed in the query string under the "data" key
	 */
	simpleAjaxGet(url: string, callback: any, dataValue?: any): void {

		var ajaxSettings = {type: 'GET', url: url};
		if (dataValue) ajaxSettings['data'] = {data: dataValue};

		$.ajax(ajaxSettings)
			.done(function (data) {
				callback(data);
			});
	}

	/**
	 * Retrieve data from localhost
	 * @param {String} url The URL to request
	 * @param {Function} callback Function to call when the ajax request returns
	 * @param {String} [dataValue] Passed in the post under the "data" key
	 */
	simpleAjaxPost(url, callback, dataValue): void {

		var ajaxSettings = {type: 'POST', url: url};
		if (dataValue) ajaxSettings['data'] = {data: dataValue};

		$.ajax(ajaxSettings)
			.done(function (data) {
				callback(data);
			});
	}

	/**
	 * Sends data to localhost
	 * @param {String} url The URL to request
	 * @param {String} [dataValue] Passed in the post under the "data" key
	 */
	simpleAjaxNoCallback(url, dataValue): void {

		var ajaxSettings = {type: 'POST', url: url};
		if (dataValue) ajaxSettings['data'] = {data: dataValue};

		$.ajax(ajaxSettings)
	}
}
