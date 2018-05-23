import axios from "axios";
import { ITool, ToolBox } from "../toolbox";

$(document).ready(() => {
    // request our model and set the controls
    axios.get("/bloom/api/book/settings").then(result => {
        var settings = result.data;

        // Only show this if we are editing a shell book. Otherwise, it's already not locked.
        if (!settings.isRecordedAsLockedDown) {
            $(".showOnlyWhenBookWouldNormallyBeLocked").css("display", "none");
            $("input[name='isTemplateBook']").prop("checked", settings.isTemplateBook);
        } else {
            $(".showOnlyIfBookIsNeverLocked").css("display", "none");
            // enhance: this is just dirt-poor binding of 1 checkbox for now
            $("input[name='unlockShellBook']").prop("checked", settings.unlockShellBook);
        }
    });
});

export function handleBookSettingCheckboxClick(clickedButton: any) {
    // read our controls and send the model back to c#
    // enhance: this is just dirt-poor serialization of checkboxes for now
    var inputs = $("#bookSettings :input");
    var o = {};
    var settings = $.map(inputs, (input, i) => {
        o[input.name] = $(input).prop("checked");
        return o;
    })[0];
    axios.post("/bloom/api/book/settings", settings);
}

// We need a minimal model to get ourselves loaded
export class BookSettings implements ITool {
    makeRootElement(): HTMLDivElement {
        throw new Error("Method not implemented.");
    }
    beginRestoreSettings(settings: string): JQueryPromise<void> {
        // Nothing to do, so return an already-resolved promise.
        var result = $.Deferred<void>();
        result.resolve();
        return result;
    }
    id(): string {
        return "bookSettings";
    }
    hasRestoredSettings: boolean;
    isAlwaysEnabled(): boolean {
        return true;
    }
    isExperimental(): boolean {
        return false;
    }
    /* tslint:disable:no-empty */ // We need these to implement the interface, but don't need them to do anything.
    configureElements(container: HTMLElement) { }
    showTool() { }
    hideTool() { }
    updateMarkup() { }
    newPageReady() { }
    detachFromPage() { }
    finishToolLocalization(pane: HTMLElement) { }
    /* tslint:enable:no-empty */
}
