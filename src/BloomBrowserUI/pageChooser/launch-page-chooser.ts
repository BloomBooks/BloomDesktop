import theOneLocalizationManager from "../lib/localizationManager/localizationManager";
// Confusingly, this function is not used by the HTML that primarily loads the JS built from this
// file (the pageChooserBundle, loaded by page-chooser-main.pug). Instead, it is imported into
// the editViewFrame and exported from there so it can be invoked directly from C#, in the context of
// the editable page, to fire off the whole process of launching the dialog which contains an iframe
// whose source loads this file. (Is there a better place for this function? It is nice to have it
// with the rest of the page-chooser code, except for the problem of belonging to the parent frame.)
// NB: this function does not have access to the PageChooser object which will eventually be created and called
// in the context of the ready function for the dialog iframe content window.
export function showAddPageDialog(forChooseLayout: boolean) {
    var theDialog;

    //reviewSlog. I don't see why the localiationManager should live on the page. Where stuff is equally relevant to all frames,
    //it should if anything belong to the root frmate (this one)
    //var parentElement = (<any>document.getElementById('page')).contentWindow;
    //var lm = parentElement.localizationManager;

    // don't show if a dialog already exists
    if ($(document).find(".ui-dialog").length) {
        return;
    }

    var key = "EditTab.AddPageDialog.Title";
    var english = "Add Page...";

    if (forChooseLayout) {
        key = "EditTab.AddPageDialog.ChooseLayoutTitle";
        english = "Choose Different Layout...";
    }

    theOneLocalizationManager.asyncGetText(key, english, "").done(title => {
        var dialogContents = CreateAddPageDiv();

        theDialog = $(dialogContents).dialog({
            //reviewslog Typescript didn't like this class: "addPageDialog",
            autoOpen: false,
            resizable: false,
            modal: true,
            width: 845,
            height: 650,
            position: {
                my: "left bottom",
                at: "left bottom",
                of: window
            },
            title: title,
            close: function() {
                $(this).remove();
                fireCSharpEvent("setModalStateEvent", "false");
            }
        });

        //TODO:  this doesn't work yet. We need to make it work, and then make it localizationManager.asyncGetText(...).done(translation => { do the insertion into the dialog });
        // theDialog.find('.ui-dialog-buttonpane').prepend("<div id='hint'>You can press ctrl+N to add the same page again, without opening this dialog.</div>");

        jQuery(document).on("click", "body > .ui-widget-overlay", () => {
            $(".ui-dialog-titlebar-close").trigger("click");
            return false;
        });
        fireCSharpEvent("setModalStateEvent", "true");
        theDialog.dialog("open");

        //parentElement.$.notify("testing notify",{});
    });
}

function CreateAddPageDiv() {
    var dialogContents = $('<div id="addPageConfig"/>').appendTo($("body"));

    // For some reason when the height is 100% we get an unwanted scroll bar on the far right.
    var html =
        '<iframe id="addPage_frame" src="/bloom/pageChooser/page-chooser-main.html" scrolling="no" style="width: 100%; height: 99%; border: none; margin: 0"></iframe>';
    dialogContents.append(html);
    return dialogContents;
}

/**
 * Fires an event for C# to handle
 * @param {String} eventName
 * @param {String} eventData
 * @param {boolean} dispatchWindow if not null, use this window's document to dispatch the event
 */
// Enhance: JT notes that this method pops up from time to time; can we consolidate?
function fireCSharpEvent(eventName, eventData, dispatchWindow?: Window) {
    var event = new MessageEvent(eventName, {
        /*'view' : window,*/ bubbles: true,
        cancelable: true,
        data: eventData
    });
    if (dispatchWindow) {
        dispatchWindow.document.dispatchEvent(event);
    } else {
        document.dispatchEvent(event);
    }
}
