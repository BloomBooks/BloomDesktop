import { ITool } from "../toolbox";
import { ToolBox } from "../toolbox";
import * as AudioRecorder from "./audioRecording";
import { theOneAudioRecorder } from "./audioRecording";

export default class TalkingBookTool implements ITool {
    makeRootElement(): HTMLDivElement {
        throw new Error("Method not implemented.");
    }
    beginRestoreSettings(settings: string): JQueryPromise<void> {
        // Nothing to do, so return an already-resolved promise.
        var result = $.Deferred<void>();
        result.resolve();
        return result;
    }

    isAlwaysEnabled(): boolean {
        return true;
    }

    isExperimental(): boolean {
        return false;
    }

    configureElements(container: HTMLElement) {}

    showTool() {
        this.showImageDescriptionsIfAny();
        AudioRecorder.initializeTalkingBookTool();
        AudioRecorder.theOneAudioRecorder.setupForRecording();
    }

    // Called when a new page is loaded.
    newPageReady() {
        AudioRecorder.theOneAudioRecorder.addAudioLevelListener(); // keeps the peak audio level monitor functioning.
    }

    hideTool() {
        // nothing to do here (if this class eventually extends our React Adaptor, this can be removed.)
    }

    detachFromPage() {
        // not quite sure how this can be called when never initialized, but if
        // we don't have the object we certainly can't use it.
        if (AudioRecorder.theOneAudioRecorder) {
            AudioRecorder.theOneAudioRecorder.removeRecordingSetup();
        }
        ToolBox.getPage().classList.remove("bloom-showImageDescriptions");
    }

    // Called whenever the user edits text.
    updateMarkup() {
        this.showImageDescriptionsIfAny();
        AudioRecorder.theOneAudioRecorder.updateMarkupAndControlsToCurrentText();
    }

    private showImageDescriptionsIfAny() {
        // If we have any image descriptions we need to show them so we can record them.
        // (Also because we WILL select them, which is confusing if they are not visible.)
        const page = ToolBox.getPage();
        var imageContainers = page.getElementsByClassName(
            "bloom-imageContainer"
        );
        for (var i = 0; i < imageContainers.length; i++) {
            const container = imageContainers[i];
            var imageDescriptions = container.getElementsByClassName(
                "bloom-imageDescription"
            );
            for (var j = 0; j < imageDescriptions.length; j++) {
                if (imageDescriptions[j].textContent.trim().length > 0) {
                    page.classList.add("bloom-showImageDescriptions");
                    return;
                }
            }
        }
    }

    id() {
        return "talkingBook";
    }

    hasRestoredSettings: boolean;

    // Some things were impossible to do i18n on via the jade/pug
    // This gives us a hook to finish up the more difficult spots
    finishToolLocalization(paneDOM: HTMLElement) {
        // So far unneeded in talkingBook
    }
}
