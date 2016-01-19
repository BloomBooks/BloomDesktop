/// <reference path="../typings/jquery/jquery.d.ts" />

/**
 * This collects javascript exceptions not handled in a try...catch block and forwards them to the server
 */
$().ready(function() {

  // the col argument may not be available if less than Gecko 31
  window.onerror = function(msg, url, line, col=0) {

    var ajaxSettings: JQueryAjaxSettings = <JQueryAjaxSettings>{type: 'POST', url: '/bloom/error'};
    ajaxSettings['data'] = {message: msg, url: url, line: line, column: col};
    $.ajax(ajaxSettings)
  };
});
