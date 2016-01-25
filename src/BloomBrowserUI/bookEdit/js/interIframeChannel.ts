/// <reference path="../../typings/jquery/jquery.d.ts" />

/**
 * Class to hold information passed between iframes
 * @constructor
 */
class interIframeChannel {

  public localizationManagerDictionary: any;
  public readableFileExtensions: string[];

  constructor() {
    this.localizationManagerDictionary = {};
    this.readableFileExtensions = ['txt', 'js', 'json'];
  }

  /**
   * Retrieve data from localhost
   * @param {String} url The URL to request
   * @param {Function} callback Function to call when the ajax request returns
   * @param [dataValue] Passed in the query string under the "data" key.
   * NOTE: If dataValue is a string, it must NOT be URI encoded.
   */
  simpleAjaxGet(url: string, callback: any, dataValue?: any): void {

    // We are calling encodeURIComponent() if dataValue is a string.
    // NOTE: We are encoding every string, so the caller should NOT encode the string.
    if (typeof dataValue === 'string')
      dataValue = encodeURIComponent(dataValue);

    var ajaxSettings: JQueryAjaxSettings = <JQueryAjaxSettings>{type: 'GET', url: url};
    if (dataValue) ajaxSettings['data'] = {data: dataValue};

    $.ajax(ajaxSettings)
      .done(function (data) {
        callback(data);
      });
  }

  /**
  * Gets the data and returns a promise
  */
  asyncGet(url: string, dataValue?: any): JQueryPromise<any>{

      // We are calling encodeURIComponent() if dataValue is a string.
      // NOTE: We are encoding every string, so the caller should NOT encode the string.
      if (typeof dataValue === 'string')
          dataValue = encodeURIComponent(dataValue);

      var ajaxSettings: JQueryAjaxSettings = <JQueryAjaxSettings>{ type: 'GET', url: url };
      if (dataValue) ajaxSettings['data'] = dataValue ;

      return $.ajax(ajaxSettings);
  }

  /*
   * This will earn you the following message in the console:
   *  "Synchronous XMLHttpRequest on the main thread is deprecated because of its detrimental effects to the end user's experience. For more help http://xhr.spec.whatwg.org/"
   */
  getValueSynchronously(url: string, parameters?: any): string {
      var ajaxSettings: JQueryAjaxSettings = <JQueryAjaxSettings>{ type: 'GET', url: url, async:false };
      if (parameters) ajaxSettings['data'] =  parameters ;
      return $.ajax(ajaxSettings).responseText;
  }


  /**
   * Retrieve data from localhost
   * @param {String} url The URL to request
   * @param {Function} callback Function to call when the ajax request returns
   * @param {Object} callbackParam Parameter passed to the callback function
   * @param [dataValue] Passed in the query string under the "data" key
   * NOTE: If dataValue is a string, it must NOT be URI encoded.
   */
  simpleAjaxGetWithCallbackParam(url: string, callback: any, callbackParam: any, dataValue?: any): void {

    // We are calling encodeURIComponent() if dataValue is a string.
    // NOTE: We are encoding every string, so the caller should NOT encode the string.
    if (typeof dataValue === 'string')
      dataValue = encodeURIComponent(dataValue);

    var ajaxSettings: JQueryAjaxSettings = <JQueryAjaxSettings>{type: 'GET', url: url};
    if (dataValue) ajaxSettings['data'] = {data: dataValue};

    $.ajax(ajaxSettings)
      .done(function (data) {
        callback(data, callbackParam);
      });
  }

  /**
   * Retrieve data from localhost
   * @param {String} url The URL to request
   * @param {Function} callback Function to call when the ajax request returns
   * @param {String} [dataValue] Passed in the post under the "data" key
   */
  simpleAjaxPost(url: string, callback: any, dataValue?: string): void {

    var ajaxSettings: JQueryAjaxSettings = <JQueryAjaxSettings>{type: 'POST', url: url};
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
  simpleAjaxNoCallback(url: string, dataValue?: string): void {

    var ajaxSettings: JQueryAjaxSettings = <JQueryAjaxSettings>{type: 'POST', url: url};
    if (dataValue) ajaxSettings['data'] = {data: dataValue};

    $.ajax(ajaxSettings)
  }

  getPageWindow(): Window {
    return (<HTMLIFrameElement>document.getElementById('page')).contentWindow;
  }

  getToolboxWindow(): Window {
    return (<HTMLIFrameElement>document.getElementById('toolbox')).contentWindow;
  }
}
