import { ITool } from "../toolbox";
import { ToolBox } from "../toolbox";
import * as AudioRecorder from "./audioRecording";
import { checkIfEnterpriseAvailable } from "../../../react_components/requiresBloomEnterprise";

export default class TalkingBookTool implements ITool {
    private enterpriseEnabled: boolean = false;

    public makeRootElement(): HTMLDivElement {
        throw new Error("Method not implemented.");
    }
    public beginRestoreSettings(settings: string): JQueryPromise<void> {
        // Nothing to do, so return an already-resolved promise.
        const result = $.Deferred<void>();
        result.resolve();
        return result;
    }

    public isAlwaysEnabled(): boolean {
        return true;
    }

    public isExperimental(): boolean {
        return false;
    }

    public configureElements(container: HTMLElement) {}

    public showTool() {
        AudioRecorder.initializeTalkingBookTool();
        checkIfEnterpriseAvailable().then(enabled => {
            this.enterpriseEnabled = enabled;
            if (enabled) {
                this.showImageDescriptionsIfAny();
            }
            AudioRecorder.theOneAudioRecorder.setupForRecording();
        });
    }

    // Called when a new page is loaded.
    public newPageReady() {
        // if this class eventually extends our React Adaptor, this can be removed.
        AudioRecorder.theOneAudioRecorder.newPageReady();
    }

    public hideTool() {
        if (AudioRecorder && AudioRecorder.theOneAudioRecorder) {
            AudioRecorder.theOneAudioRecorder.hideTool();
        }
    }

    public detachFromPage() {
        // not quite sure how this can be called when never initialized, but if
        // we don't have the object we certainly can't use it.
        if (AudioRecorder.theOneAudioRecorder) {
            AudioRecorder.theOneAudioRecorder.removeRecordingSetup();
        }
        const page = ToolBox.getPage();
        if (page) {
            page.classList.remove("bloom-showImageDescriptions");
        }
    }

    // Called whenever the user edits text.
    public updateMarkup() {
        if (this.enterpriseEnabled) {
            this.showImageDescriptionsIfAny();
        }
        AudioRecorder.theOneAudioRecorder.updateMarkupAndControlsToCurrentText();
    }

    private showImageDescriptionsIfAny() {
        // If we have any image descriptions we need to show them so we can record them.
        // (Also because we WILL select them, which is confusing if they are not visible.)
        const page = ToolBox.getPage();
        if (!page) {
            return;
        }
        const imageContainers = page.getElementsByClassName(
            "bloom-imageContainer"
        );
        for (let i = 0; i < imageContainers.length; i++) {
            const container = imageContainers[i];
            const imageDescriptions = container.getElementsByClassName(
                "bloom-imageDescription"
            );
            for (let j = 0; j < imageDescriptions.length; j++) {
                const text = imageDescriptions[j].textContent;
                if (text && text.trim().length > 0) {
                    page.classList.add("bloom-showImageDescriptions");
                    return;
                }
            }
        }
    }

    public id() {
        return "talkingBook";
    }

    public hasRestoredSettings: boolean;

    // Some things were impossible to do i18n on via the jade/pug
    // This gives us a hook to finish up the more difficult spots
    public finishToolLocalization(paneDOM: HTMLElement) {
        // So far unneeded in talkingBook
    }
}
