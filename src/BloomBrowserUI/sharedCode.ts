/// <reference path="typings/jquery/jquery.d.ts" />

// This file holds some code so global that it's not limited to one particular tab.

/**
 * Class to with methods related to invoking bloom help
 * @constructor
 */
class BloomHelp {
    /**
   * Opens the application help topic
   * @param topic
   * @returns {boolean} Returns false to prevent navigation if link clicked.
   */
    static show(topic: string): boolean {

        Ajax.simpleNoCallback('/bloom/help', topic);
        return false;
    }
}

class Ajax {

  /**
   * Sends data to localhost
   * @param {String} url The URL to request
   * @param {String} [dataValue] Passed in the post under the "data" key
   */
  static simpleNoCallback(url: string, dataValue?: string): void {

    var ajaxSettings: JQueryAjaxSettings = <JQueryAjaxSettings>{type: 'POST', url: url};
    if (dataValue) ajaxSettings['data'] = {data: dataValue};

    $.ajax(ajaxSettings)
  }
}
