import { hideImageDescriptions } from "../imageDescription/imageDescriptionUtils";
import { kBloomCanvasClass } from "../overlay/canvasElementUtils";
import { beginLoadSynphonySettings } from "../readers/readerTools";
import { getTheOneToolbox, ITool } from "../toolbox";
import { ToolBox } from "../toolbox";
import { getAudioRecorder } from "./audioRecording";
import * as AudioRecorder from "./audioRecording";

export default class TalkingBookTool implements ITool {
    imageUpdated(img: HTMLImageElement | undefined): void {
        // No action needed for this tool
    }
    public makeRootElement(): HTMLDivElement {
        throw new Error("Method not implemented.");
    }
    public beginRestoreSettings(settings: string): JQueryPromise<void> {
        // Nothing to do except that we need the sentence ending punctuation settings
        // from the leveled reader tool.  (We share sentence parsing via libSynphony.)
        return beginLoadSynphonySettings();
    }

    public isAlwaysEnabled(): boolean {
        return true;
    }

    public isExperimental(): boolean {
        return false;
    }

    public requiresToolId(): boolean {
        return false;
    }

    public configureElements(container: HTMLElement) {
        // one-time setup whether or not the tool is open.
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
        await getAudioRecorder().setupForRecordingAsync();
    }

    // Called when a new page is loaded.
    public async newPageReady(): Promise<void> {
        this.showImageDescriptionsIfAny();
        const pageReadyPromise = getAudioRecorder().handleNewPageReady(
            TalkingBookTool.deshroudPhraseDelimiters,
        );
        return pageReadyPromise;
    }

    // Replace any span marked as a "bloom-audio-split-marker" with a plain "|".
    // This allows internal processing to proceed without having to contend with
    // that markup.
    public static deshroudPhraseDelimiters(page: HTMLElement | null) {
        if (!page) return;
        const delimitingSpans = page.getElementsByClassName(
            "bloom-audio-split-marker",
        );
        // delimitingSpans is a live collection that changes as we remove spans.
        // So we stick all the values into a real array to ensure we process all of them.
        const spansToReplace = Array.from(delimitingSpans);
        spansToReplace.forEach((span) => {
            span.replaceWith(page.ownerDocument.createTextNode("|"));
        });
    }

    // Replace each phrase delimiter bar ("|") with a span containing a zero-width space
    // and a class that will result in it being invisible as well as zero-width outside
    // of this tool.
    public static enshroudPhraseDelimiters(page: HTMLElement | null) {
        if (!page || !page.hasChildNodes()) return;
        // page.childNodes is a live collection that changes as we add/remove nodes.
        // So we stick all the values into a real array for processing.
        const children = Array.from(page.childNodes);
        // Processing from the end of the list should prevent problems.
        for (let i = 0; i < children.length; ++i) {
            const node = children[i];
            switch (node.nodeType) {
                case Node.ELEMENT_NODE:
                    this.enshroudPhraseDelimiters(node as HTMLElement);
                    break;
                case Node.TEXT_NODE:
                    {
                        const originalText = node.textContent;
                        if (originalText?.includes("|")) {
                            const matches =
                                originalText.match(/(([^\|]+)|(\|))/g);
                            if (matches && matches.length > 0) {
                                const newNodes = matches.map((val) => {
                                    if (val === "|") {
                                        const newSpan =
                                            page.ownerDocument.createElement(
                                                "span",
                                            );
                                        newSpan.classList.add(
                                            "bloom-audio-split-marker",
                                        );
                                        return newSpan as Node;
                                    } else {
                                        return val;
                                    }
                                });
                                node.replaceWith(...newNodes);
                            }
                        }
                    }
                    break;
            }
        }
    }

    public hideTool() {
        const audioRecorder = getAudioRecorder();
        if (audioRecorder) {
            audioRecorder.handleToolHiding();
        }
    }

    public detachFromPage() {
        const audioRecorder = getAudioRecorder();
        // not quite sure how this can be called when never initialized, but if
        // we don't have the object we certainly can't use it.
        if (audioRecorder) {
            audioRecorder.removeRecordingSetup();
        }
        const page = ToolBox.getPage();
        if (page) {
            hideImageDescriptions(page);
            TalkingBookTool.enshroudPhraseDelimiters(page);
        }
    }

    // Called whenever the user edits text.
    public async updateMarkupAsync(): Promise<() => void> {
        return getAudioRecorder().getUpdateMarkupAction();
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
            getAudioRecorder()?.setShowingImageDescriptions(false);
            return;
        }
        const bloomCanvases = page.getElementsByClassName(kBloomCanvasClass);

        for (let i = 0; i < bloomCanvases.length; i++) {
            const bloomCanvas = bloomCanvases[i];
            const imageDescriptions = bloomCanvas.getElementsByClassName(
                "bloom-imageDescription",
            );
            for (let j = 0; j < imageDescriptions.length; j++) {
                const text = imageDescriptions[j].textContent;
                if (text && text.trim().length > 0) {
                    getAudioRecorder()?.setShowingImageDescriptions(true);
                    return;
                }
            }
        }
        getAudioRecorder()?.setShowingImageDescriptions(false);
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
