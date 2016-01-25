/// <reference path="../../typings/jqueryui/jqueryui.d.ts" />
/// <reference path="decodableReader/synphonyApi.ts" />
/// <reference path="decodableReader/readerToolsModel.ts" />
/// <reference path="../../typings/jqueryui/jqueryui.d.ts" />

import 'jquery-ui/jquery-ui-1.10.3.custom.min.js';
import 'jquery.i18n.custom.js';
import "jquery.text-markup.js"; //onOnce, which probably doesn't belong in there

/**
 * The html code for a check mark character
 * @type String
 */
var checkMarkString = '&#10004;';

var showingPanel = false;
var savedSettings: string;

var keypressTimer: any = null;

export interface ITabModel {
    restoreSettings(settings: string);
    configureElements(container: HTMLElement);
    showTool();
    hideTool();
    updateMarkup();
    name(): string;
}

// Class that represents the whole toolbox. Gradually we will move more functionality in here.
export class ToolBox {
    toolboxIsShowing() { return showingPanel; }
    configureElementsForTools(container: HTMLElement) {
        for (var i = 0; i < tabModels.length; i++) {
            tabModels[i].configureElements(container);
            // the toolbox itself handles keypresses in order to manage the process
            // of giving each tool a chance to update things when the user stops typing
            // (while maintaining the selection if at all possible).
            $(container).find('.bloom-editable').keypress(function () {
                doKeypressMarkup();
            });
       }
    }
    /**
     * Fires an event for C# to handle
     * @param {String} eventName
     * @param {String} eventData
     */
    static fireCSharpToolboxEvent(eventName: string, eventData: string) {

    var event = new MessageEvent(eventName, {'bubbles' : true, 'cancelable' : true, 'data' : eventData});
    document.dispatchEvent(event);
    }

    static getTabModels() { return tabModels;}
}

var toolbox = new ToolBox();

// Array of models, typically one for each tab. The code for each tab inserts an appropriate model
// into this array in order to be interact with the overall toolbox code.
var tabModels: ITabModel[] = [];
var currentTool: ITabModel;

/**
 * Handles the click event of the divs in Settings.htm that are styled to be check boxes.
 * @param chkbox
 */
function showOrHidePanel_click(chkbox) {

    var panel = $(chkbox).data('panel');

    if (chkbox.innerHTML === '') {

        chkbox.innerHTML = checkMarkString;
        ToolBox.fireCSharpToolboxEvent('saveToolboxSettingsEvent', "active\t" + chkbox.id + "\t1");
        if (panel) {
            showingPanel = true;
            requestPanel(chkbox.id, panel, null, null, null);
        }
    }
    else {
        chkbox.innerHTML = '';
        ToolBox.fireCSharpToolboxEvent('saveToolboxSettingsEvent', "active\t" + chkbox.id + "\t0");
        $('*[data-panelId]').filter(function() { return $(this).attr('data-panelId') === panel; }).remove();
    }

    resizeToolbox();
}

/**
* Called by C# to restore user settings
*/
export function restoreToolboxSettings(settings: string) {
    savedSettings = settings;
    var pageFrame = getPageFrame();
    if (pageFrame.contentWindow.document.readyState === 'loading') {
        // We can't finish restoring settings until the main document is loaded, so arrange to call the next stage when it is.
        $(pageFrame.contentWindow.document).ready(e => restoreToolboxSettingsWhenPageReady(settings));
        return;
    }
    restoreToolboxSettingsWhenPageReady(settings); // not loading, we can proceed immediately.
}


function restoreToolboxSettingsWhenPageReady(settings: string) {
    var page = getPage();
    if (!page || page.length === 0) {
        // Somehow, despite firing this function when the document is supposedly ready,
        // it may not really be ready when this is first called. If it doesn't even have a body yet,
        // we need to try again later.
        setTimeout(e => restoreToolboxSettingsWhenPageReady(settings), 100);
        return;
    }
    // Once we have a valid page, we can proceed to the next stage.
    restoreToolboxSettingsWhenCkEditorReady(settings);
}

function restoreToolboxSettingsWhenCkEditorReady(settings: string) {
    if ((<any>getPageFrame().contentWindow).CKEDITOR) {
        var editorInstances = (<any>getPageFrame().contentWindow).CKEDITOR.instances;
        // Somewhere in the process of initializing ckeditor, it resets content to what it was initially.
        // This wipes out (at least) our page initialization.
        // To prevent this we hold our initialization until CKEditor has done initializing.
        // If any instance on the page (e.g., one per div) is not ready, wait until all are.
        // (The instances property leads to an object in which a field editorN is defined for each
        // editor, so we just loop until some value of N which doesn't yield an editor instance.)
        for (var i = 1;; i++) {
            var instance = editorInstances['editor' + i];
            if (instance == null) {
                if (i === 0) {
                    // no instance at all...if one is later created, get us invoked.
                    (<any>this.getPageFrame().contentWindow).CKEDITOR.on('instanceReady', e => restoreToolboxSettingsWhenCkEditorReady(settings));
                    return;
                }
                break; // if we get here all instances are ready
            }
            if (!instance.instanceReady) {
                instance.on('instanceReady', e => restoreToolboxSettingsWhenCkEditorReady(settings));
                return;
            }
        }
    }
    // OK, CKEditor is done (or page doesn't use it), we can finally do the real initialization.
    var opts = settings;
    var currentPanel = opts['current'] || '';

    // Before we set stage/level, as it initializes them to 1.
    setCurrentPanel(currentPanel);

    // Note: the bulk of restoring the settings (everything but which if any panel is active)
    // is done when a tool becomes current.
}

function getPageFrame(): HTMLIFrameElement {
    return <HTMLIFrameElement>parent.window.document.getElementById('page');
}

    // The body of the editable page, a root for searching for document content.
function getPage(): JQuery {
    var page = getPageFrame();
    if (!page) return null;
    return $(page.contentWindow.document.body);
}

function switchTool(newToolName: string)
{
    var newTool = null;
    for (var i = 0; i < tabModels.length; i++) {
        if (tabModels[i].name() === newToolName) {
            newTool = tabModels[i];
        }
    }
    if (currentTool !== newTool) {
        if (currentTool)
            currentTool.hideTool();
        if (newTool && (<HTMLInputElement>$(parent.window.document).find('#pure-toggle-right').get(0)).checked) {
            // If we're activating this tool for the first time, restore its settings.
            if (!newTool.hasRestoredSettings) {
                newTool.hasRestoredSettings = true;
                newTool.restoreSettings(savedSettings);
            }
            newTool.showTool();
        }
        currentTool = newTool;
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
                // break from the each() loop
                return false;
            }
            return true; // continue the each() loop
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
        var newToolName = null;
        if (ui.newHeader.attr('data-panelId')) {
            newToolName = ui.newHeader.attr('data-panelId').toString();
            ToolBox.fireCSharpToolboxEvent('saveToolboxSettingsEvent', "current\t" + ui.newHeader.attr('data-panelId').toString());
        } else {
            ToolBox.fireCSharpToolboxEvent('saveToolboxSettingsEvent', "current\t");
        }
        switchTool(newToolName);
    });
    //alert('switching to ' + currentPanel);
    //setTimeout(e => switchTool(currentPanel), 700);
    switchTool(currentPanel);
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

function doKeypressMarkup(): void {

    // BL-599: "Unresponsive script" while typing in text.
    // The function setTimeout() returns an integer, not a timer object, and therefore it does not have a member
    // function called "clearTimeout." Because of this, the jQuery method $.isFunction(keypressTimer.clearTimeout)
    // will always return false (since "this.keypressTimer.clearTimeout" is undefined) and the result is a new 500
    // millisecond timer being created every time the doKeypress method is called, but none of the pre-existing timers
    // being cleared. The correct way to clear a timeout is to call clearTimeout(), passing it the integer returned by
    // the function setTimeout().

    //if (this.keypressTimer && $.isFunction(this.keypressTimer.clearTimeout)) {
    //  this.keypressTimer.clearTimeout();
    //}
    if(keypressTimer)
      clearTimeout(keypressTimer);

    keypressTimer = setTimeout(function () {

        // This happens 500ms after the user stops typing.
        var page: HTMLIFrameElement = <HTMLIFrameElement>parent.window.document.getElementById('page');
        if (!page) return; // unit testing?

        var selection: Selection = page.contentWindow.getSelection();
        var current: Node = selection.anchorNode;
        var active = <HTMLDivElement>$(selection.anchorNode).closest('div').get(0);
        if (!active || selection.rangeCount > 1 || (selection.rangeCount == 1 && !selection.getRangeAt(0).collapsed)) {
            return; // don't even try to adjust markup while there is some complex selection
        }

        var myRange: Range = selection.getRangeAt(0).cloneRange();
        myRange.setStart(active, 0);
        var offset: number = myRange.toString().length;

        // In case the IP is somewhere like after the last <br> or between <br>s,
        // its anchorNode is the div itself, or perhaps one of its spans, and we want to try to put it back
        // in a comparable position. -1 marks a selection that is at a text level.
        // other values count the <br> elements immediately before the selection.
        // I am hoping it doesn't happen that there are <br>s at multiple levels.
        // Note that the newly marked up version will have any <br>s at the top level only (children of the div).
        var divBrCount: number = -1;
        if (current.nodeType !== 3) {
            divBrCount = 0;
            // endoffset counts the number of childNodes that the selection is after.
            // We want to know how many <br> nodes are between it and the previous non-empty node.
            for (var k = myRange.endOffset - 1; k >= 0; k--) {
                if (current.childNodes[k].localName === 'br') divBrCount++;
                else if (current.childNodes[k].textContent.length > 0) break;
            }
        }

        var atStart: boolean = myRange.endOffset === 0;

        if (currentTool) currentTool.updateMarkup();

        // Now we try to restore the selection at the specified position.
        EditableDivUtils.makeSelectionIn(active, offset, divBrCount, atStart);

        // clear this value to prevent unnecessary calls to clearTimeout() for timeouts that have already expired.
        keypressTimer = null;
    }, 500);
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

function showToolboxChanged(showing: boolean): void {
    if (currentTool) {
        if (showing) currentTool.hideTool();
        else currentTool.showTool();
    } else {
        // starting up for the very first time in this book...no tool is current,
        // so select and properly initialize the first one.
        switchTool($('#toolbox').find(('> h3')).first().attr('data-panelId'));
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

$(parent.window.document).ready(function() {
    $(parent.window.document).find('#pure-toggle-right').change(function() { showToolboxChanged(!this.checked); });
})