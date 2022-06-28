import { getTheOneToolbox, ITool } from "../toolbox";
import { ToolBox } from "../toolbox";
import * as AudioRecorder from "./audioRecording";

export default class TalkingBookTool implements ITool {
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

    public configureElements(container: HTMLElement) {
        // Implements ITool interface, but we don't need to do anything
    }

    // When are showTool, newPageReady, and updateMarkup called?
    // Some scenarios:
    // * Open the toolbox and Talking Book shows up  - showTool, newPageReady
    // * Open a book and Talking Book tool automatically opens - showTool, newPageReady
    // * Creating a new page while tool is open - newPageReady, newPageReady (again)
    // * Changing to an existing page while tool is open - same as above.
    // * Typing in a text box while tool is open - updateMarkup
    // * Close the toolbox: hideTool()
    // * hit the Toolbox's "More" switcher: hideTool()
    // * Switching from a different tool to Talking Book Tool - showTool, newPageReady
    // * Add a new text box using Origami ("Change Layout"), then turn off the Origami Editor: newPageReady, updateMarkup
    public async showTool(): Promise<void> {
        // BL-7588 There used to be a enterprise callback that delayed image descriptions and setup until
        // the initialize function had completed, now that it isn't there we need to treat the initialize
        // as the asynchronous method it is.
        await AudioRecorder.initializeTalkingBookToolAsync();
        await AudioRecorder.theOneAudioRecorder.setupForRecordingAsync();
    }

    // Called when a new page is loaded.
    public async newPageReady(): Promise<void> {
        this.showImageDescriptionsIfAny();
        return AudioRecorder.theOneAudioRecorder.newPageReady(
            this.isImageDescriptionToolActive()
        );
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
    public async updateMarkupAsync(): Promise<() => void> {
        this.showImageDescriptionsIfAny();
        return AudioRecorder.theOneAudioRecorder.getUpdateMarkupAction();
    }

    public updateMarkup() {
        throw "not implemented, use updateMarkupAsync";
    }

    public isUpdateMarkupAsync(): boolean {
        return true;
    }

    private isImageDescriptionToolActive(): boolean {
        return getTheOneToolbox().isToolActive("imageDescriptionTool");
    }

    private showImageDescriptionsIfAny() {
        // If we have any image descriptions we need to show them so we can record them.
        // (BL-8515) Unless the image description tool is not currently active.
        const page = ToolBox.getPage();
        if (!page) {
            return;
        }
        if (!this.isImageDescriptionToolActive()) {
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
