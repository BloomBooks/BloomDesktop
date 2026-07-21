import { getImageFromCanvasElement } from "../bloomImages";
import {
    kBackgroundImageClass,
    kCanvasElementClass,
} from "../../toolbox/canvas/canvasElementConstants";

export type ComicEditingSuspendedState =
    | "none"
    | "forDrag"
    | "forTool"
    | "forJqueryResize"
    | "forGamePlayMode";

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
    // process that affects layout is happening (current example: origami layout tool is active),
    // or because we're testing a bloom game.
    // When in one of the latter states, it may be inferred that isComicEditingOn was true when
    // suspendComicEditing was called, that it is now false, and that resumeComicEditing should
    // turn it on again.
    private suspendedState: ComicEditingSuspendedState = "none";

    private splitterResizeObservers: ResizeObserver[] = [];
    private draggingSplitter = false;

    public constructor(host: ICanvasElementEditingSuspensionHost) {
        this.host = host;
    }

    public isSuspended = (): boolean => {
        return this.suspendedState !== "none";
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
        if (!this.host.getIsCanvasElementEditingOn()) {
            return;
        }
        this.host.turnOffCanvasElementEditing();
        if (forWhat === "forDrag" || forWhat === "forJqueryResize") {
            this.startDraggingSplitter();
        }

        if (forWhat === "forGamePlayMode") {
            const allCanvasElements = Array.from(
                document.getElementsByClassName(kCanvasElementClass),
            );
            allCanvasElements.forEach((element) => {
                const editables = Array.from(
                    element.getElementsByClassName("bloom-editable"),
                );
                editables.forEach((editable) => {
                    editable.removeAttribute("contenteditable");
                });
            });
        }

        this.suspendedState = forWhat;
    };

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
        if (this.suspendedState === "forGamePlayMode") {
            const allCanvasElements = Array.from(
                document.getElementsByClassName(kCanvasElementClass),
            );
            allCanvasElements.forEach((element) => {
                const editables = Array.from(
                    element.getElementsByClassName("bloom-editable"),
                );
                editables.forEach((editable) => {
                    editable.setAttribute("contenteditable", "true");
                });
            });
            this.host.setupControlFrame();
        }
        this.suspendedState = "none";
        this.host.turnOnCanvasElementEditing();
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
