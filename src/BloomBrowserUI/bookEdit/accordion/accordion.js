
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
            requestPanel(chkbox.id, panel)
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
    var currentPanel = opts['current'] || '';
    var state;
    var panels = [];

    if (opts['showPE'])
        panels.push(['showPE', 'PageElements']);

    if (opts['showDRT'])
        panels.push(['showDRT', 'DecodableRT']);

    if (opts['showLRT'])
        panels.push(['showLRT', 'LeveledRT']);

    if (opts['decodableState']) {
        state = libsynphony.dbGet('drt_state');
        if (!state) state = new DRTState();
        state.stage = 0 + opts['decodableState'];
        libsynphony.dbSet('drt_state', state);
    }

    if (opts['leveledState']) {
        state = libsynphony.dbGet('drt_state');
        if (!state) state = new DRTState();
        state.level = 0 + opts['leveledState'];
        libsynphony.dbSet('drt_state', state);
    }

    loadPanelsAndSetCurrent(panels, currentPanel);
}

/**
 * This function requests one panel, and then sets itself up to be called again after the panel is loaded. If there
 * are no more panels to load it activates the last panel displayed, as long as it isn't the "More" panel.
 * @param {Array} panels An array of arrays.
 * @param {String} currentPanel
 */
function loadPanelsAndSetCurrent(panels, currentPanel) {

    // if there are still panels to load, load the next one
    if (panels && (panels.length > 0)) {

        // remove the first panel from the array
        var panel = panels.shift();

        // request the panel from localhost
        requestPanel(panel[0], panel[1], loadPanelsAndSetCurrent, panels, currentPanel);

        return;
    }

    // there are no more panels, so set the current one
    var idx = '0';
    var accordion = $('#accordion');

    if (currentPanel) {

        // find the index of the panel with the 'current' id
        accordion.find('> h3').each(function() {
            if ($(this).data('panelId') === currentPanel) {

                // the index is the last segment of the element id
                idx = this.id.substr(this.id.lastIndexOf('-') + 1);

                // break from the each() loop
                return false;
            }
            return true;
        });
    }

    // turn off animation
    var ani = accordion.accordion('option', 'animate');
    accordion.accordion('option', 'animate', false);

    // the index must be passed as an int, a string will not work
    accordion.accordion('option', 'active', parseInt(idx));

    // turn animation back on
    accordion.accordion('option', 'animate', ani);

    // when a panel is activated, save which it is so state can be restored when Bloom is restarted.
    accordion.onOnce('accordionactivate.accordion', function(event, ui) {

        if (ui.newHeader.data('panelId'))
            fireCSharpAccordionEvent('saveAccordionSettingsEvent', "current\t" + ui.newHeader.data('panelId').toString());
        else
            fireCSharpAccordionEvent('saveAccordionSettingsEvent', "current\t");
    });
}

/**
 * Requests a panel from localhost and loads it into the accordion.
 * @param {String} checkBoxId
 * @param {String} panelId
 * @param {Function} loadNextCallback
 * @param {Array} panels
 * @param {String} currentPanel
 */
function requestPanel(checkBoxId, panelId, loadNextCallback, panels, currentPanel) {

    var chkBox = document.getElementById(checkBoxId);
    if (chkBox) {
        chkBox.innerHTML = checkMarkString;

        var panelUrl = '/bloom/bookEdit/accordion/' + panelId + '/' + panelId + '.htm';
        var ajaxSettings = {type: 'GET', url: panelUrl};

        $.ajax(ajaxSettings)
            .done(function (data) {
                loadAccordionPanel(data, panelId);
                loadNextCallback(panels, currentPanel)
            });
    }
}

var resizeTimer;
function resizeAccordion() {
    var windowHeight = $(window).height();
    var root = $(".accordionRoot");
    // Set accordion container height to fit in new window size
    // Then accordion Resize() will adjust it to fit the container
    root.height(windowHeight);
    BloomAccordion.Resize();
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

    // if requested, open the panel that was just inserted
    if (showingPanel) {
        showingPanel = false;
        var id = tab.attr('id');
        id = parseInt(id.substr(id.lastIndexOf('_')));
        accordion.accordion('option', 'active', id);

        // when a panel is activated, save which it is so state can be restored when Bloom is restarted.
        accordion.onOnce('accordionactivate.accordion', function(event, ui) {

            if (ui.newHeader.data('panelId'))
                fireCSharpAccordionEvent('saveAccordionSettingsEvent', "current\t" + ui.newHeader.data('panelId').toString());
            else
                fireCSharpAccordionEvent('saveAccordionSettingsEvent', "current\t");
        });
    }
}

$(document).ready(function () {
    new BloomAccordion(); // have to create this somewhere to get it initialized.
    resizeAccordion(); // Make sure it gets run once, at least.

    // Now bind the window's resize function to the accordion resizer
    $(window).bind('resize', function () {
        clearTimeout(resizeTimer); // resizeTimer variable is defined outside of ready function
        resizeTimer = setTimeout(resizeAccordion, 100);
    });
});