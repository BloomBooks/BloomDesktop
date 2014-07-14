
/**
 * The html code for a check mark character
 * @type String
 */
var checkMarkString = '&#10004;';

/**
 * Fires an event for C# to handle
 * @param {String} eventName
 * @param {String} eventData
 */
function fireCSharpAccordionEvent(eventName, eventData) {

    var event = new MessageEvent(eventName, {'view' : window, 'bubbles' : true, 'cancelable' : true, 'data' : eventData});
    document.dispatchEvent(event);
}

/**
 * Handles the click event of the divs in Settings.htm that are styled to be check boxes.
 * @param chkbox
 */
function showOrHidePanel_click(chkbox) {

    var panel = $(chkbox).data('panel');

    if (chkbox.innerHTML === '') {

        chkbox.innerHTML = checkMarkString;
        fireCSharpAccordionEvent('saveAccordionSettingsEvent', chkbox.id + "\t1");
        if (panel) fireCSharpAccordionEvent('loadAccordionPanelEvent', panel);
    }
    else {
        chkbox.innerHTML = '';
        fireCSharpAccordionEvent('saveAccordionSettingsEvent', chkbox.id + "\t0");
        $('*:data(panelId)').filter(function() { return $(this).data('panelId') === panel; }).remove();
    }
}

/**
 * Called by C# to restore user settings
 * @param {String} settings
 */
function restoreAccordionSettings(settings) {

    var opts = JSON.parse(settings);

    if (opts['showPE'])
        requestPanel('showPE', 'PageElements');

    if (opts['showDRT'])
        requestPanel('showDRT', 'DecodableRT');

    if (opts['showLRT'])
        requestPanel('showLRT', 'LeveledRT');

    // set the current panel
    if (opts['current']) {

        var accordion = $('#accordion');

        // find the index of the panel with the 'current' id
        accordion.find('> h3').each(function() {
            if ($(this).data('panelId') === opts['current']) {

                // the index is the last segment of the element id
                var idx = this.id.substr(this.id.lastIndexOf('-') + 1);

                // turn off animation
                var ani = accordion.accordion('option', 'animate');
                accordion.accordion('option', 'animate', false);

                // the index must be passed as an int, a string will not work
                accordion.accordion('option', 'active', parseInt(idx));

                // turn animation back on
                accordion.accordion('option', 'animate', ani);

                // break from the each() loop
                return false;
            }
            return true;
        });
    }
}

function requestPanel(checkBoxId, panelId) {

    var chkBox = document.getElementById(checkBoxId);
    if (chkBox) {
        chkBox.innerHTML = checkMarkString;

        // expects C# to call 'loadAccordionPanel' with the html for the new panel
        fireCSharpAccordionEvent('loadAccordionPanelEvent', panelId);
    }
}

function loadAccordionPanel(newContent, panelId) {

    var elements = $.parseHTML(newContent, document, true);

    $.each(elements, function() {

            $(this).data('panelId', panelId);
            $(this).insertBefore('#accordion-settings-header');
    });

    var accordion = $('#accordion');
    accordion.accordion('refresh');

    accordion.onOnce('accordionactivate.accordion', function(event, ui) {

        if (ui.newHeader.data('panelId'))
            fireCSharpAccordionEvent('saveAccordionSettingsEvent', "current\t" + ui.newHeader.data('panelId').toString());
        else
            fireCSharpAccordionEvent('saveAccordionSettingsEvent', "current\t");
    });

}
