import { BloomApi } from "../../../utils/bloomApi";
import { ITool } from "../toolbox";

$(document).ready(() => {
    // request our model and set the controls
    BloomApi.get("book/settings", result => {
        const settings = result.data;

        // Nothing to do for now; we don't actually have any book settings.
    });
});

export function handleBookSettingCheckboxClick(clickedButton: any) {
    // read our controls and send the model back to c#
    // enhance: this is just dirt-poor serialization of checkboxes for now
    const inputs = $("#bookSettings :input");
    const o = {};
    const settings = $.map(inputs, (input, i) => {
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
        const result = $.Deferred<void>();
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
    /* eslint-disable @typescript-eslint/no-empty-function */
    public configureElements(container: HTMLElement) {}
    public showTool() {}
    public hideTool() {}
    public updateMarkup() {}
    public async updateMarkupAsync() {
        return () => {};
    }
    public isUpdateMarkupAsync(): boolean {
        return false;
    }
    public newPageReady() {}
    public detachFromPage() {}
    public finishToolLocalization(pane: HTMLElement) {}
    /* eslint-enable @typescript-eslint/no-empty-function */
}
