import { BloomApi } from "../../../utils/bloomApi";
import { ITool } from "../toolbox";

$(document).ready(() => {
    // request our model and set the controls
    BloomApi.get("book/settings", result => {
        var settings = result.data;

        // Only show this if we are editing a shell book. Otherwise, it's already not locked.
        if (!settings.isRecordedAsLockedDown) {
            $(".showOnlyWhenBookWouldNormallyBeLocked").css("display", "none");
            $("input[name='isTemplateBook']").prop(
                "checked",
                settings.isTemplateBook
            );
        } else {
            $(".showOnlyIfBookIsNeverLocked").css("display", "none");
            // enhance: this is just dirt-poor binding of 1 checkbox for now
            $("input[name='unlockShellBook']").prop(
                "checked",
                settings.unlockShellBook
            );
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
    BloomApi.post("book/settings", settings);
}

// We need a minimal model to get ourselves loaded
export class BookSettings implements ITool {
    public makeRootElement(): HTMLDivElement {
        throw new Error("Method not implemented.");
    }
    public beginRestoreSettings(settings: string): JQueryPromise<void> {
        // Nothing to do, so return an already-resolved promise.
        var result = $.Deferred<void>();
        result.resolve();
        return result;
    }
    public id(): string {
        return "bookSettings";
    }
    public hasRestoredSettings: boolean;
    public isAlwaysEnabled(): boolean {
        return true;
    }
    public isExperimental(): boolean {
        return false;
    }
    // We need these to implement the interface, but don't need them to do anything.
    /* tslint:disable:no-empty */ public configureElements(
        container: HTMLElement
    ) {}
    public showTool() {}
    public hideTool() {}
    public updateMarkup() {}
    public isUpdateMarkupAsync(): boolean {
        return false;
    }
    public newPageReady() {}
    public detachFromPage() {}
    public finishToolLocalization(pane: HTMLElement) {}
    /* tslint:enable:no-empty */
}
