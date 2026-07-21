// Keyboard interactions for moving/deleting the active canvas element.
// We currently use CanvasSnapProvider for step size only; movement still uses
// CanvasElementManager constraints to keep elements visible in the parent canvas.
import { kBackgroundImageClass } from "../../toolbox/canvas/canvasElementConstants";
import { CanvasSnapProvider } from "./CanvasSnapProvider";

const kArrowMoveByKey: Record<string, { dx: number; dy: number }> = {
    ArrowUp: { dx: 0, dy: -1 },
    ArrowDown: { dx: 0, dy: 1 },
    ArrowLeft: { dx: -1, dy: 0 },
    ArrowRight: { dx: 1, dy: 0 },
};

export interface ICanvasElementKeyboardActions {
    deleteCurrentCanvasElement: () => void;
    moveActiveCanvasElement: (
        dx: number,
        dy: number,
        event?: KeyboardEvent,
    ) => void;
    getActiveCanvasElement: () => HTMLElement | null;
}

export class CanvasElementKeyboardProvider {
    private actions: ICanvasElementKeyboardActions;
    private snapProvider: CanvasSnapProvider;

    constructor(
        actions: ICanvasElementKeyboardActions,
        snapProvider: CanvasSnapProvider,
    ) {
        this.actions = actions;
        this.snapProvider = snapProvider;
        document.addEventListener("keydown", this.handleKeyDown);
    }

    public dispose(): void {
        document.removeEventListener("keydown", this.handleKeyDown);
    }

    private handleKeyDown = (event: KeyboardEvent): void => {
        const stepSize = this.snapProvider.getMinimumStepSize(event);

        // Check if the event target is an input field or textarea, or contenteditable.
        // If so, we don't want to interfere with typing.
        // (For canvas elements containing text, the target element will be something
        // inside the contentEditable bloom-editable if it's in text edit mode, so
        // the check for isContentEditable will prevent interfering with typing.
        // When the text element is selected but not in edit mode, the target will be the
        // canvas element itself, which is not contentEditable, so we can move it.
        // We don't currently have inputs or textareas in canvas elements, but the event
        // handler for this is applied to the whole document, so we need to avoid
        // doing preventDefault anywhere we might be typing.)

        const targetElement = event.target as HTMLElement;
        if (
            targetElement.tagName === "INPUT" ||
            targetElement.tagName === "TEXTAREA" ||
            targetElement.isContentEditable
        ) {
            return;
        }
        // If the active element is a background image, we don't want to use the keyboard
        // to delete it or move it.  (BL-14737)
        const activeElement = this.actions.getActiveCanvasElement();
        if (
            activeElement &&
            activeElement.classList.contains(kBackgroundImageClass)
        ) {
            return;
        }
        if (event.key === "Delete" || event.key === "Backspace") {
            this.actions.deleteCurrentCanvasElement();
            event.preventDefault(); // Prevent default browser back navigation on Backspace
            return;
        }

        const movement = kArrowMoveByKey[event.key];
        if (!movement) {
            return;
        }

        this.actions.moveActiveCanvasElement(
            movement.dx * stepSize,
            movement.dy * stepSize,
            event,
        );
        event.preventDefault();
    };
}
