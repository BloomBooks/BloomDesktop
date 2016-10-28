/// <reference path="../../typings/jqueryui/jqueryui.d.ts" />
///<reference path="../../typings/axios/axios.d.ts"/>

import 'jquery-ui/jquery-ui-1.10.3.custom.min.js';
import '../../lib/jquery.i18n.custom';
import "../../lib/jquery.onSafe";
import axios = require('axios');
import {EditableDivUtils} from '../js/editableDivUtils';

/**
 * The html code for a check mark character
 * @type String
 */
var checkMarkString = '&#10004;';

var savedSettings: string;

var keypressTimer: any = null;

export interface ITabModel {
    beginRestoreSettings(settings: string): JQueryPromise<void>;
    configureElements(container: HTMLElement);
    showTool();
    hideTool();
    updateMarkup();
    name(): string; // without trailing 'Tool'!
    hasRestoredSettings: boolean;
}

// Class that represents the whole toolbox. Gradually we will move more functionality in here.
export class ToolBox {
    toolboxIsShowing() { return (<HTMLInputElement>$(parent.window.document).find('#pure-toggle-right').get(0)).checked; }
    configureElementsForTools(container: HTMLElement) {
        for (var i = 0; i < tabModels.length; i++) {
            tabModels[i].configureElements(container);
            // the toolbox itself handles keypresses in order to manage the process
            // of giving each tool a chance to update things when the user stops typing
            // (while maintaining the selection if at all possible).
            /* Note: BL-3900: "Decodable & Talking Book tools delete text after longpress".
               In that bug, longpress.replacePreviousLetterWithText() would delete back
               to the start of the current markup span (e.g. a sentence in
               Talking Book, or a non-decodable word in Decodable Reader).
               The current fix is to trigger markup on keydown, rather than keyup or keypress.
               ****This is exactly the opposite of what we would expect****

               If we trigger on keyup here, the sequence looks right but longpress will eat up the span.
               Here's the sequence:
                    longpress: replacePreviousLetterWithText()
                    Toolbox: setting timer markup
                    Toolbox: doing markup
                    Toolbox: Restoring Selection after markup

                So the mystery in the above case is, what is going on with the dom and longpress.replacePreviousLetterWithText()
                such that replacePreviousLetterWithText() replaces a bunch of characters instead of 1 character?

                Counterintuitively, if we instead trigger on keydown here, the settimeout()
                doesn't fire until longpress is all done and all is well:
                    1) Toolbox: setting timer markup
                    2) longpress: replacePreviousLetterWithText()
                    3) Toolbox: doing markup
                    4) Toolbox: Restoring Selection after markup

                (3) is delayed presumably because (2) is still in the event-handling loop. That's fine. But the
                mystery then was: why does it help longpress.replacePreviousLetterWithText() to not eat up a whole span?

                It turns out that when longpress goes to get the selection,
                in the keyup or keypress senarios, the selection's startContainer is the markup span (which has the #text
                node inside of it). So then a deleteContents() wiped out *all* the text in the span (I've added a check for
                that scenario so that if it happens again, longpress will fail instead of deleting text).
                However in the keydown case, we get a #text node for the selection, as expected. My hypothesis is that by doing
                the work during the keyDown event, some code somewhere runs when the key goes up, restoring a good selection.
                So when longpress is used, it doesn't trip over the span.

                For now I'm just going to commit the fix and if someday we revisit this, maybe another piece of the
                puzzle will emerge.
            */

            $(container).find('.bloom-editable').keydown(function (event) {
                if (event.ctrlKey) {
                    // this is check is a workaround for BL-3490, but when doKeypressMarkup() get's fixed, it should be removed
                    // because as is, we're not updating markup when you paste in text
                    console.log("Skipping markup on paste because of faulty insertion logic. See BL-3490");
                    return;
                }
                //don't do markup on cursor keys
                if (event.keyCode >= 37 && event.keyCode <= 40) {
                    // this is check is another workaround for one scenario of BL-3490, but one that, as far as I can tell makes sense.
                    // if all they did was move the cursor, we don't need to look at markup.
                    console.log("skipping markup on arrow key");
                    return;
                }
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
    top.document.dispatchEvent(event);
    }

    static getTabModels() { return tabModels; }
}

var toolbox = new ToolBox();

export function getTheOneToolbox() {
    return toolbox;
}

// Array of models, typically one for each tab. The code for each tab inserts an appropriate model
// into this array in order to be interact with the overall toolbox code.
var tabModels: ITabModel[] = [];
var currentTool: ITabModel;

/**
 * Handles the click event of the divs in Settings.htm that are styled to be check boxes.
 * @param chkbox
 */
export function showOrHidePanel_click(chkbox) {

    var panel = $(chkbox).data('panel');

    if (chkbox.innerHTML === '') {
        chkbox.innerHTML = checkMarkString;
        ToolBox.fireCSharpToolboxEvent('saveToolboxSettingsEvent', "active\t" + chkbox.id + "\t1");
        if (panel) {
            beginAddPanel(chkbox.id, panel);
        }
    }
    else {
        chkbox.innerHTML = '';
        ToolBox.fireCSharpToolboxEvent('saveToolboxSettingsEvent', "active\t" + chkbox.id + "\t0");
        $('*[data-panelId]').filter(function () { return $(this).attr('data-panelId') === panel; }).remove();
    }

    resizeToolbox();
}


export function restoreToolboxSettings() {
    axios.get<any>("/bloom/api/toolbox/settings").then(result => {
        savedSettings = result.data;
        var pageFrame = getPageFrame();
        if (pageFrame.contentWindow.document.readyState === 'loading') {
            // We can't finish restoring settings until the main document is loaded, so arrange to call the next stage when it is.
            $(pageFrame.contentWindow.document).ready(e => restoreToolboxSettingsWhenPageReady(result.data));
            return;
        }
        restoreToolboxSettingsWhenPageReady(result.data); // not loading, we can proceed immediately.
    });
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
        for (var i = 1; ; i++) {
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

// Remove any markup the toolbox is inserting (called before saving page)
export function removeToolboxMarkup() {
    if (currentTool != null) {
        currentTool.hideTool();
    }
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

function switchTool(newToolName: string) {
    ToolBox.fireCSharpToolboxEvent('saveToolboxSettingsEvent', "current\t" + newToolName); // Have Bloom remember which tool is active. (Might be none)
    var newTool = null;
    if (newToolName) {
        for (var i = 0; i < tabModels.length; i++) {
            // the newToolName comes from meta.json and we've changed our minds a few times about
            // whether it should end in 'Tool' so what's in the meta.json might have it or not.
            // For robustness we will recognize any tool name that starts with the (no -Tool)
            // name we're looking for.
            if (newToolName.startsWith(tabModels[i].name())) {
                newTool = tabModels[i];
            }
        }
    }
    if (currentTool !== newTool) {
        if (currentTool)
            currentTool.hideTool();
        activateTool(newTool);
        currentTool = newTool;
    }
}

function activateTool(newTool: ITabModel) {
    if (newTool && toolbox.toolboxIsShowing()) {
        // If we're activating this tool for the first time, restore its settings.
        if (!newTool.hasRestoredSettings) {
            newTool.hasRestoredSettings = true;
            newTool.beginRestoreSettings(savedSettings).then(() => newTool.showTool());
        } else {
            newTool.showTool();
        }
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
        toolbox.find('> h3').each(function () {
            if ($(this).attr('data-panelId') === currentPanel) {
                // the index is the last segment of the element id
                idx = this.id.substr(this.id.lastIndexOf('-') + 1);
                // break from the each() loop
                return false;
            }
            return true; // continue the each() loop
        });
    } else {
        // Leave idx at 0, and update currentPanel to the corresponding ID.
        currentPanel = toolbox.find('> h3').first().attr('data-panelId');
    }

    // turn off animation
    var ani = toolbox.accordion('option', 'animate');
    toolbox.accordion('option', 'animate', false);

    // the index must be passed as an int, a string will not work.
    // (May be worth retaining a note that in an earlier version of accordion, H3 elements had indexes 1, 3, 5, 7,
    // since an ID was generated for the intermediate content divs also. We need 0, 1, 2, 3.
    // Currently however it seems the generated IDs are 0, 1, and 2 as would be expected; the 'more' header doesn't
    // seem to get a numbered ID which is not a problem since it can't be a persisted current tool.)
    var toolIndex = parseInt(idx);
    toolbox.accordion('option', 'active', toolIndex);

    // turn animation back on
    toolbox.accordion('option', 'animate', ani);

    // when a panel is activated, save its data-panelId so state can be restored when Bloom is restarted.
    // We do this after we actually set the initial panel, because setting the intial panel may not CHANGE
    // the active panel (if it's already the one we want, typically the first), so we can't rely on
    // the activate event happening in the initial call. Instead, we make SURE to call it for the
    // panel we are making active.
    toolbox.onSafe('accordionactivate.toolbox', function (event, ui) {
        var newToolName = "";
        if (ui.newHeader.attr('data-panelId')) {
            newToolName = ui.newHeader.attr('data-panelId').toString();
        }
        switchTool(newToolName);
    });
    //alert('switching to ' + currentPanel + " which has index " + toolIndex);
    //setTimeout(e => switchTool(currentPanel), 700);
    switchTool(currentPanel);
}

/**
 * Requests a panel from localhost and loads it into the toolbox.
 * This is used when the user ticks a previously unticked checkbox of a tool.
 * Normally that job goes to an equivalent c# function. Enhance: remove the c# one.
 */
// these last three parameters were never used: function requestPanel(checkBoxId, panelId, loadNextCallback, panels, currentPanel) {
function beginAddPanel(checkBoxId: string, panelId: string): Promise<void> {
    var chkBox = document.getElementById(checkBoxId);
    if (chkBox) {
        chkBox.innerHTML = checkMarkString;

        var subpath = {
            'decodableReaderTool': 'readers/decodableReader/decodableReaderToolboxPanel.html',
            'leveledReaderTool': 'readers/leveledReader/leveledReaderToolboxPanel.html',
            'bookSettingsTool': 'bookSettings/bookSettingsToolboxPanel.html',
            'toolboxSettingsTool': 'toolboxSettingsTool/toolboxSettingsToolboxPanel.html'
        }
        return axios.get<any>("/bloom/bookEdit/toolbox/" + subpath[panelId]).then(result => {
            loadToolboxPanel(result.data, panelId);
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
    if (keypressTimer)
        clearTimeout(keypressTimer);
    keypressTimer = setTimeout(function () {

        // This happens 500ms after the user stops typing.
        var page: HTMLIFrameElement = <HTMLIFrameElement>parent.window.document.getElementById('page');
        if (!page) return; // unit testing?

        //don't need to do any of this if there is no tool that will be adding markup anyway.
        if (!currentTool || !toolbox.toolboxIsShowing()) {
            return;
        }

        var selection: Selection = page.contentWindow.getSelection();
        var current: Node = selection.anchorNode;
        var active = <HTMLDivElement>$(selection.anchorNode).closest('div').get(0);
        if (!active || selection.rangeCount > 1 || (selection.rangeCount == 1 && !selection.getRangeAt(0).collapsed)) {
            return; // don't even try to adjust markup while there is some complex selection
        }

        // the hard thing about all this is preserving the user's insertion point while we change the actual
        // html out from under them to add/remove markup.
        // ckeditor specific discussion: http://stackoverflow.com/questions/16835365/set-cursor-to-specific-position-in-ckeditor
        // This "bookmark" approach makes that easy:
        // We insert a dummy element where the insert point is. Later when we do the markup,
        // we'll find the bookmark again, put the selection there, and remove this element.
        // The problem with this approach is that when the user is fixing an existing word, the markup
        // will see our bookmark as a word-breaking element. For example, if I type "houze" and go
        // to fix that z, the markup routine is going to see "hous"-bookmark-"e". When the user
        // clicks away, the markup will be redone and fixed. So this is a known tradeoff; we get
        // more reliable insertion-point-preservation, at the cost of some temporarily inaccurate
        // markup.
        const editableDiv = $(selection.anchorNode).parents(".bloom-editable")[0];
        const ckeditorOfThisBox = (<any>editableDiv).bloomCkEditor;
        const ckeditorSelection = ckeditorOfThisBox.getSelection();

        // there is also createBookmarks2(), which avoids actually inserting anything. That has the
        // advantage that changing a character in the middle of a word will allow the entire word to
        // be evaluated by the markup routine. However, testing shows that the cursor then doesn't
        // actually go back to where it was: it gets shifted to the right.
        const bookmarks = ckeditorSelection.createBookmarks(true);
        currentTool.updateMarkup();

        //set the selection to wherever our bookmark node ended up
        //NB: in BL-3900: "Decodable & Talking Book tools delete text after longpress", it was here,
        //restoring the selection, that we got interference with longpress's replacePreviousLetterWithText(),
        // in some way that is still not understood. This was fixed by changing all this to trigger on
        // a different event (keydown instead of keypress).
        ckeditorOfThisBox.getSelection().selectBookmarks(bookmarks);

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

    var toolboxElt = $('#toolbox');

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
    var insertBefore = toolboxElt.children().filter(function () { return $(this).data('order') > order; }).first();

    // Insert now.
    tab.insertBefore(insertBefore);
    div.insertBefore(insertBefore);

    toolboxElt.accordion('refresh');

    // if requested, open the panel that was just inserted
    if (toolbox.toolboxIsShowing()) {
        var id = tab.attr('id');
        var tabNumber = parseInt(id.substr(id.lastIndexOf('_')));
        toolboxElt.accordion('option', 'active', tabNumber); // must pass as integer
    }
}

function showToolboxChanged(wasShowing: boolean): void {
    ToolBox.fireCSharpToolboxEvent('saveToolboxSettingsEvent', "visibility\t" + (wasShowing ? "" : "visible"));
    if (currentTool) {
        if (wasShowing) currentTool.hideTool();
        else activateTool(currentTool);
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

$(parent.window.document).ready(function () {
    $(parent.window.document).find('#pure-toggle-right').change(function () { showToolboxChanged(!this.checked); });
})