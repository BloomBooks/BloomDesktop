
/**
 * The html code for a check mark character
 * @type String
 */
var checkMarkString = '&#10004;';

var showingPanel = false;

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
        if (panel) {
            showingPanel = true;
            fireCSharpAccordionEvent('loadAccordionPanelEvent', panel);
        }
    }
    else {
        chkbox.innerHTML = '';
        fireCSharpAccordionEvent('saveAccordionSettingsEvent', chkbox.id + "\t0");
        $('*:data(panelId)').filter(function() { return $(this).data('panelId') === panel; }).remove();
    }

    resizeAccordion();
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

/**
 * Adds one panel to the accordion
 * @param {String} newContent
 * @param {String} panelId
 */
function loadAccordionPanel(newContent, panelId) {

    var parts = $($.parseHTML(newContent, document, true));
    var accordion = $('#accordion');

    // expect parts to have 2 items, an h3 and a div
    if (parts.length < 2) return;

    // get the accordion panel tab/button
    var tab = parts.filter('h3').first();

    // Get the order. If no order, set to top (zero)
    var order = tab.data('order');
    if (!order && (order !== 0)) order = 0;

    // get the panel content div
    var div = parts.filter('div').first();

    // Where to insert the new panel?
    // NOTE: there will always be at least one panel, the "More..." panel, so there will always be at least one panel
    // in the accordion. And the "More..." panel will have the highest order so it is always at the bottom of the stack.
    var insertBefore = accordion.children().filter(function() { return $(this).data('order') > order; }).first();

    // Insert now.
    // NOTE: tag each of the items with the "panelId" so they are easier to locate when it is time to remove them.
    tab.data('panelId', panelId);
    tab.insertBefore(insertBefore);
    div.data('panelId', panelId);
    div.insertBefore(insertBefore);

    accordion.accordion('refresh');
    if (showingPanel) {
        showingPanel = false;
        var count = accordion.find('> h3').length;
        if (count > 1)
            accordion.accordion('option', 'active', count - 2);
    }
    
    // when a panel is activated, save which it is so state can be restored when Bloom is restarted.
    accordion.onOnce('accordionactivate.accordion', function(event, ui) {

        if (ui.newHeader.data('panelId'))
            fireCSharpAccordionEvent('saveAccordionSettingsEvent', "current\t" + ui.newHeader.data('panelId').toString());
        else
            fireCSharpAccordionEvent('saveAccordionSettingsEvent', "current\t");
    });

}
