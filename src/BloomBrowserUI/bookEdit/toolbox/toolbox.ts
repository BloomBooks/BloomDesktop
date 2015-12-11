/// <reference path="../../lib/jquery-ui.d.ts" />
/// <reference path="decodableReader/synphonyApi.ts" />
/// <reference path="decodableReader/readerToolsModel.ts" />

/**
 * The html code for a check mark character
 * @type String
 */
var checkMarkString = '&#10004;';

var showingPanel = false;

interface ITabModel {
    restoreSettings(settings: string);
    configureElements(container: HTMLElement);
    showTool(ui: any);
    hideTool(ui: any);
    name(): string;
}

// Class that represents the whole toolbox. Gradually we will move more functionality in here.
class ToolBox {
    toolboxIsShowing() { return showingPanel; }
    configureElementsForTools(container: HTMLElement) {
        for (var i = 0; i < tabModels.length; i++) {
            tabModels[i].configureElements(container);
        }
    }
}

var toolbox = new ToolBox();

// Array of models, typically one for each tab. The code for each tab inserts an appropriate model
// into this array in order to be interact with the overall toolbox code.
var tabModels: ITabModel[] = [];
var currentTool: ITabModel;

/**
 * Fires an event for C# to handle
 * @param {String} eventName
 * @param {String} eventData
 */
function fireCSharpToolboxEvent(eventName: string, eventData: string) {

    var event = new MessageEvent(eventName, {'bubbles' : true, 'cancelable' : true, 'data' : eventData});
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
        fireCSharpToolboxEvent('saveToolboxSettingsEvent', "active\t" + chkbox.id + "\t1");
        if (panel) {
            showingPanel = true;
            requestPanel(chkbox.id, panel, null, null, null);
        }
    }
    else {
        chkbox.innerHTML = '';
        fireCSharpToolboxEvent('saveToolboxSettingsEvent', "active\t" + chkbox.id + "\t0");
        $('*[data-panelId]').filter(function() { return $(this).attr('data-panelId') === panel; }).remove();
    }

    resizeToolbox();
}

/**
* Called by C# to restore user settings
*/
function restoreToolboxSettings(settings:string) {

    var opts = settings;
    var currentPanel = opts['current'] || '';

    // Before we set stage/level, as it initializes them to 1.
    setCurrentPanel(currentPanel);

    // Allow each tab to restore its own settings
    for (var i = 0; i < tabModels.length; i++) {
        tabModels[i].restoreSettings(settings);
    }
}



/**
 * This function attempts to activate the panel whose "data-panelId" attribute is equal to the value
 * of "currentPanel" (the last panel displayed).
 * @param {String} currentPanel
 */
function setCurrentPanel(currentPanel) {

    // NOTE: panels without a "data-panelId" attribute (such as the More panel) cannot be the "currentPanel."
    var idx = '0';
    var toolbox = $('#toolbox');

    if (currentPanel) {

        // find the index of the panel whose "data-panelId" attribute equals the value of "currentPanel"
        toolbox.find('> h3').each(function() {
            if ($(this).attr('data-panelId') === currentPanel) {

                // the index is the last segment of the element id
                idx = this.id.substr(this.id.lastIndexOf('-') + 1);

                // set the markup type to the current panel
                if (model) {
                    model.setMarkupType(parseInt(this.dataset['markuptype']));
                    setTimeout(function() { model.doMarkup(); }, 500);
                }

                // break from the each() loop
                return false;
            }
            return true;
        });
    }

    // turn off animation
    var ani = toolbox.accordion('option', 'animate');
    toolbox.accordion('option', 'animate', false);

    // the index must be passed as an int, a string will not work
    toolbox.accordion('option', 'active', parseInt(idx));

    // turn animation back on
    toolbox.accordion('option', 'animate', ani);

    // when a panel is activated, save its data-panelId so state can be restored when Bloom is restarted.
    toolbox.onOnce('accordionactivate.toolbox', function (event, ui) {
        var newTool = null;
        if (ui.newHeader.attr('data-panelId')) {
            var newToolName = ui.newHeader.attr('data-panelId').toString();
            for (var i = 0; i < tabModels.length; i++) {
                if (tabModels[i].name() === newToolName) {
                    newTool = tabModels[i];
                }
            }
            fireCSharpToolboxEvent('saveToolboxSettingsEvent', "current\t" + ui.newHeader.attr('data-panelId').toString());
        } else {
            fireCSharpToolboxEvent('saveToolboxSettingsEvent', "current\t");
        }
        if (currentTool !== newTool) {
            if (currentTool)
                currentTool.hideTool(ui);
            if (newTool)
                newTool.showTool(ui);
            currentTool = newTool;
        }
    });
}

/**
 * Requests a panel from localhost and loads it into the toolbox.
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

        // The panelIDs all end in 'Tool' but the containing file and folder names don't have this.
        var fileAndFolderName = panelId.substring(0, panelId.length - 4);

        var panelUrl = '/bloom/bookEdit/toolbox/' + fileAndFolderName + '/' + fileAndFolderName + '.htm';
        var ajaxSettings = {type: 'GET', url: panelUrl};

        $.ajax(ajaxSettings)
            .done(function (data) {
                loadToolboxPanel(data, panelId);
                if (typeof loadNextCallback === 'function')
                    loadNextCallback(panels, currentPanel);
            });
    }
}

var resizeTimer;
function resizeToolbox() {
    var windowHeight = $(window).height();
    var root = $(".toolboxRoot");
    // Set toolbox container height to fit in new window size
    // Then toolbox Resize() will adjust it to fit the container
    root.height(windowHeight - 25); // 25 is the top: value set for div.toolboxRoot in toolbox.less
    $("#toolbox").accordion("refresh");
}

/**
 * Adds one panel to the toolbox
 * @param {String} newContent
 * @param {String} panelId
 */
function loadToolboxPanel(newContent, panelId) {
    var parts = $($.parseHTML(newContent, document, true));

    parts.filter('*[data-i18n]').localize();
    parts.find('*[data-i18n]').localize();

    var toolbox = $('#toolbox');

    // expect parts to have 2 items, an h3 and a div
    if (parts.length < 2) return;

    // get the toolbox panel tab/button
    var tab = parts.filter('h3').first();

    // Get the order. If no order, set to top (zero)
    var order = tab.data('order');
    if (!order && (order !== 0)) order = 0;

    // get the panel content div
    var div = parts.filter('div').first();

    // Where to insert the new panel?
    // NOTE: there will always be at least one panel, the "More..." panel, so there will always be at least one panel
    // in the toolbox. And the "More..." panel will have the highest order so it is always at the bottom of the stack.
    var insertBefore = toolbox.children().filter(function() { return $(this).data('order') > order; }).first();

    // Insert now.
    tab.insertBefore(insertBefore);
    div.insertBefore(insertBefore);

    toolbox.accordion('refresh');

    // if requested, open the panel that was just inserted
    if (showingPanel) {
        showingPanel = false;
        var id = tab.attr('id');
        var tabNumber = parseInt(id.substr(id.lastIndexOf('_')));
        toolbox.accordion('option', 'active', tabNumber); // must pass as integer
    }
}

$(document).ready(function () {
    $("#toolbox").accordion({
        heightStyle: "fill"
    });
    resizeToolbox(); // Make sure it gets run once, at least.
    $('body').find('*[data-i18n]').localize(); // run localization

    // Now bind the window's resize function to the toolbox resizer
    $(window).bind('resize', function () {
        clearTimeout(resizeTimer); // resizeTimer variable is defined outside of ready function
        resizeTimer = setTimeout(resizeToolbox, 100);
    });
});