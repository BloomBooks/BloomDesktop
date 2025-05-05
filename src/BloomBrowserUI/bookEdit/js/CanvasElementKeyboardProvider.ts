// Originally this was wired into CanvasSnapProvider.ts, but we're going to do that PR separately and later.
// And the way it was wired in, just using the grid size, may not be enough. We may need to ask the snap provider
// to give us the snap location. We'll see.
//import { CanvasSnapProvider } from "./CanvasSnapProvider";

export interface ICanvasElementKeyboardActions {
    deleteCurrentCanvasElement: () => void;
    moveActiveCanvasElement: (
        dx: number,
        dy: number,
        event?: KeyboardEvent
    ) => void;
}

export class CanvasElementKeyboardProvider {
    private actions: ICanvasElementKeyboardActions;
    //    private snapProvider: CanvasSnapProvider;

    constructor(
        actions: ICanvasElementKeyboardActions
        //        snapProvider: CanvasSnapProvider
    ) {
        this.actions = actions;
        //        this.snapProvider = snapProvider;
        document.addEventListener("keydown", this.handleKeyDown);
    }

    public dispose(): void {
        document.removeEventListener("keydown", this.handleKeyDown);
    }

    private handleKeyDown = (event: KeyboardEvent): void => {
        const stepSize = 1; // TODO: this.snapProvider.getMinimumStepSize(event);

        // Check if the event target is an input field or textarea, or contenteditable.
        // If so, we don't want to interfere with typing.

        const targetElement = event.target as HTMLElement;
        if (
            targetElement.tagName === "INPUT" ||
            targetElement.tagName === "TEXTAREA" ||
            targetElement.isContentEditable
        ) {
            return;
        }
        switch (event.key) {
            case "Delete":
            case "Backspace": // Often used interchangeably with Delete
                this.actions.deleteCurrentCanvasElement();
                event.preventDefault(); // Prevent default browser back navigation on Backspace
                break;
            case "ArrowUp":
                this.actions.moveActiveCanvasElement(0, -stepSize, event); // Move up by 1 pixel (or unit)
                event.preventDefault();
                break;
            case "ArrowDown":
                this.actions.moveActiveCanvasElement(0, stepSize, event); // Move down by 1 pixel (or unit)
                event.preventDefault();
                break;
            case "ArrowLeft":
                this.actions.moveActiveCanvasElement(-stepSize, 0, event); // Move left by 1 pixel (or unit)
                event.preventDefault();
                break;
            case "ArrowRight":
                this.actions.moveActiveCanvasElement(stepSize, 0, event); // Move right by 1 pixel (or unit)
                event.preventDefault();
                break;
            default:
                // Ignore other keys
                break;
        }
    };
}
