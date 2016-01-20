/// <reference path="typings/jquery/jquery.d.ts" />

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
        // This may work eventually, but fetch is not supported until FF 39
        //fetch('/bloom/help', { method: "POST", body: topic });
        var ajaxSettings: JQueryAjaxSettings = <JQueryAjaxSettings>{ type: 'POST', url: '/bloom/help' };
        ajaxSettings['data'] = { data: topic };

        $.ajax(ajaxSettings);
        return false;
    }
}
