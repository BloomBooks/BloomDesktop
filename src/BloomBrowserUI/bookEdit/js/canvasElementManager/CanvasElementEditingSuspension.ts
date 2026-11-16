import { getImageFromCanvasElement } from "../bloomImages";
import {
    kBackgroundImageClass,
    kCanvasElementClass,
} from "../../toolbox/canvas/canvasElementConstants";

export type ComicEditingSuspendedState =
    | "none"
    | "forDrag"
    | "forTool"
    | "forJqueryResize";

export interface ICanvasElementEditingSuspensionHost {
    getIsCanvasElementEditingOn: () => boolean;

    getAllBloomCanvasesOnPage: () => HTMLElement[];
    adjustBackgroundImageSize: (
        bloomCanvas: HTMLElement,
        backgroundCanvasElement: HTMLElement,
        useSizeOfNewImage: boolean,
    ) => void;
    adjustChildrenIfSizeChanged: (bloomCanvas: HTMLElement) => void;

    turnOffCanvasElementEditing: () => void;
    turnOnCanvasElementEditing: () => void;
    setupControlFrame: () => void;
}

export class CanvasElementEditingSuspension {
    private host: ICanvasElementEditingSuspensionHost;

    // Notes that comic editing either has not been suspended...isComicEditingOn might be true or false...
    // or that it was suspended because of a drag in progress that might affect page layout
    // (current example: mouse is down over an origami splitter), or because some longer running
    // process that affects layout is happening (current example: origami layout tool is active).
    // When in one of the latter states, it may be inferred that isComicEditingOn was true when
    // suspendComicEditing was called, that it is now false, and that resumeComicEditing should
    // turn it on again.
    private suspendedState: ComicEditingSuspendedState = "none";
    // Whether comic editing is suspended because we're testing a bloom game (its Play tab). This is
    // kept apart from suspendedState because a game page can be played with any tool open, so Play
    // mode and a tool's suspension (the Talking Book tool showing image descriptions, say) overlap,
    // in either order, and neither may end the other. Editing is on only when neither holds.
    private inGamePlayMode = false;

    private splitterResizeObservers: ResizeObserver[] = [];
    private draggingSplitter = false;

    public constructor(host: ICanvasElementEditingSuspensionHost) {
        this.host = host;
    }

    public isSuspended = (): boolean => {
        return this.suspendedState !== "none" || this.inGamePlayMode;
    };

    public startDraggingSplitter = (): void => {
        this.host.getAllBloomCanvasesOnPage().forEach((bloomCanvas) => {
            const backgroundCanvasElement = bloomCanvas.getElementsByClassName(
                kBackgroundImageClass,
            )[0] as HTMLElement;
            if (backgroundCanvasElement) {
                // These two attributes are what the resize observer will mess with to make
                // the background resize as the splitter moves. We will restore them in
                // endDraggingSplitter so the code that adjusts all the canvas elements has the
                // correct starting size.
                backgroundCanvasElement.setAttribute(
                    "data-oldStyle",
                    backgroundCanvasElement.getAttribute("style") ?? "",
                );
                const img = getImageFromCanvasElement(backgroundCanvasElement);
                img?.setAttribute(
                    "data-oldStyle",
                    img.getAttribute("style") ?? "",
                );
                const resizeObserver = new ResizeObserver(() => {
                    this.host.adjustBackgroundImageSize(
                        bloomCanvas,
                        backgroundCanvasElement,
                        false,
                    );
                });
                resizeObserver.observe(bloomCanvas);
                this.splitterResizeObservers.push(resizeObserver);
            }
        });
    };

    public endDraggingSplitter = (): void => {
        this.host.getAllBloomCanvasesOnPage().forEach((bloomCanvas) => {
            const backgroundCanvasElement = bloomCanvas.getElementsByClassName(
                kBackgroundImageClass,
            )[0] as HTMLElement;
            if (backgroundCanvasElement) {
                // We need to remove the results of the continuous adjustments so that we can make the change again,
                // but this time adjust all the other canvas elements with it.
                backgroundCanvasElement.setAttribute(
                    "style",
                    backgroundCanvasElement.getAttribute("data-oldStyle") ?? "",
                );
                backgroundCanvasElement.removeAttribute("data-oldStyle");
                const img = getImageFromCanvasElement(backgroundCanvasElement);
                img?.setAttribute(
                    "style",
                    img.getAttribute("data-oldStyle") ?? "",
                );
                img?.removeAttribute("data-oldStyle");
            }
            while (this.splitterResizeObservers.length) {
                this.splitterResizeObservers.pop()?.disconnect();
            }
        });
    };

    public suspendComicEditing = (
        forWhat: "forDrag" | "forTool" | "forGamePlayMode" | "forJqueryResize",
    ): void => {
        if (forWhat === "forGamePlayMode") {
            this.startGamePlayMode();
            return;
        }
        if (!this.host.getIsCanvasElementEditingOn()) {
            // Already off. If that is for Play mode, a tool's suspension must still outlast it; a
            // drag's is over before Play mode could end.
            if (this.inGamePlayMode && forWhat === "forTool") {
                this.suspendedState = "forTool";
            }
            return;
        }
        this.host.turnOffCanvasElementEditing();
        if (forWhat === "forDrag" || forWhat === "forJqueryResize") {
            this.startDraggingSplitter();
        }
        this.suspendedState = forWhat;
    };

    // Resume comic editing after a suspension for anything but a game's Play mode, which only the
    // game's tab control may end (endGamePlayModeSuspension). A game page can be played with any tool
    // open, so a tool that suspended editing for itself and now resumes it (the Talking Book tool,
    // image descriptions) may find the page in Play mode, and editing then stays off until Play ends.
    public resumeComicEditing = (): void => {
        if (this.suspendedState === "none") {
            return;
        }
        if (
            this.suspendedState === "forDrag" ||
            this.suspendedState === "forJqueryResize"
        ) {
            this.endDraggingSplitter();
        }
        if (this.suspendedState === "forTool") {
            this.setupSplitterEventHandling();
        }
        this.suspendedState = "none";
        if (!this.inGamePlayMode) {
            this.host.turnOnCanvasElementEditing();
        }
    };

    private startGamePlayMode = (): void => {
        if (this.inGamePlayMode || !this.isSuspendedOrEditingOn()) {
            return;
        }
        this.inGamePlayMode = true;
        this.setCanvasEditablesEditable(false);
        if (this.host.getIsCanvasElementEditingOn()) {
            this.host.turnOffCanvasElementEditing();
        }
    };

    // End the suspension of comic editing for a game's Play mode. If a tool still has editing
    // suspended, it stays suspended for that.
    public endGamePlayModeSuspension = (): void => {
        if (!this.inGamePlayMode) {
            return;
        }
        this.inGamePlayMode = false;
        this.setCanvasEditablesEditable(true);
        if (this.suspendedState === "none") {
            this.host.setupControlFrame();
            this.host.turnOnCanvasElementEditing();
        }
    };

    // Comic editing is something to suspend: it is on, or suspended already for something else.
    private isSuspendedOrEditingOn = (): boolean =>
        this.suspendedState !== "none" ||
        this.host.getIsCanvasElementEditingOn();

    // Play mode makes the text in canvas elements uneditable, so that a tester's clicks and drags
    // play the game instead of editing it.
    private setCanvasEditablesEditable = (editable: boolean): void => {
        document
            .querySelectorAll(`.${kCanvasElementClass} .bloom-editable`)
            .forEach((bloomEditable) => {
                if (editable) {
                    bloomEditable.setAttribute("contenteditable", "true");
                } else {
                    bloomEditable.removeAttribute("contenteditable");
                }
            });
    };

    private dividerMouseDown = (_ev: Event) => {
        if (this.suspendedState === "forTool") {
            // We're in change layout mode. We want to get the usual behavior of any
            // existing images while dragging the splitter, but we don't need to turn
            // off comic editing since it already is.
            this.draggingSplitter = true;
            this.startDraggingSplitter();
        } else {
            // Unless we're suspended for some other reason, this will call startDraggingSplitter
            // after turning stuff off.
            this.suspendComicEditing("forDrag");
        }
    };

    private documentMouseUp = (ev: Event) => {
        if (this.suspendedState === "forDrag") {
            // The mousedown was in an origami slider.
            // Clean up and don't let the mouse up affect anything else.
            // (Note: we're not stopping IMMEDATE propagation, so another mouseup handler
            // on the document can remove the origami-drag class.)
            ev.preventDefault();
            ev.stopPropagation();
            setTimeout(() => {
                // in timeout to be sure that another mouseup handler will have removed
                // the origami-drag class from the body, so we can get the right
                // resize behavior when turning back on.
                this.resumeComicEditing();
            }, 0);
        } else if (this.draggingSplitter) {
            // dragging the splitter while in origami mode. We need to clean up
            // in the way resume normally does
            this.draggingSplitter = false;
            this.endDraggingSplitter();
            for (const bloomCanvas of this.host.getAllBloomCanvasesOnPage()) {
                this.host.adjustChildrenIfSizeChanged(bloomCanvas);
            }
        }
    };

    public setupSplitterEventHandling = (): void => {
        // When dragging origami sliders, turn comical off.
        // With this, we get some weirdness during dragging: canvas element text moves, but
        // the canvas elements do not. But everything clears up when we turn it back on afterwards.
        // Without it, things are even weirder, and the end result may be weird, too.
        // The comical canvas does not change size as the slider moves, and things may end
        // up in strange states with canvas elements cut off where the boundary used to be.
        // It's possible that we could do better by forcing the canvas to stay the same
        // size as the bloom-canvas, but I'm very unsure how resizing an active canvas
        // containing objects will affect ComicalJs and the underlying PaperJs.
        // It should be pretty rare to resize an image after adding canvas elements, so I think it's
        // better to go with this, which at least gives a predictable result.
        // Note: we don't ever need to remove these; they can usefully hang around until
        // we load some other page. (We don't turn off comical when we hide the tool, since
        // the canvas elements are still visible and editable, and we need it's help to support
        // all the relevant behaviors and keep the canvas elements in sync with the text.)
        // Because we're adding a fixed method, not a local function, adding multiple
        // times will not cause duplication.
        Array.from(
            document.getElementsByClassName("split-pane-divider"),
        ).forEach((d) =>
            d.addEventListener("mousedown", this.dividerMouseDown),
        );
        document.addEventListener("mouseup", this.documentMouseUp, {
            capture: true,
        });
    };
}
