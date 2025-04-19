import { CanvasSnapProvider } from "./CanvasSnapProvider";

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
    private snapProvider: CanvasSnapProvider;

    constructor(
        actions: ICanvasElementKeyboardActions,
        snapProvider: CanvasSnapProvider
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

// Example Usage (would typically be in a different file where CanvasElementManager is instantiated):
/*
import { CanvasElementManager } from "./CanvasElementManager";
import { CanvasElementKeyboardProvider } from "./CanvasElementKeyboardProvider";

const canvasElementManager = new CanvasElementManager();

// Define the move function (this needs to be implemented in CanvasElementManager or similar)
const moveElement = (dx: number, dy: number) => {
    const activeElement = canvasElementManager.getActiveElement();
    if (activeElement) {
        const currentLeft = parseFloat(activeElement.style.left || "0");
        const currentTop = parseFloat(activeElement.style.top || "0");
        const newLeft = currentLeft + dx;
        const newTop = currentTop + dy;

        // Here you would likely call a method on canvasElementManager
        // to actually move the element and update its state/ComicalJS, etc.
        // For now, just logging.
        console.log(`Moving element to ${newLeft}, ${newTop}`);

        // Example of how you might update the style directly (simplistic):
        // activeElement.style.left = newLeft + "px";
        // activeElement.style.top = newTop + "px";
        // canvasElementManager.doNotifyChange(); // Notify about the change
    }
};


const keyboardProvider = new CanvasElementKeyboardProvider({
    deleteCurrentCanvasElement: canvasElementManager.deleteCurrentCanvasElement.bind(canvasElementManager),
    moveActiveCanvasElement: moveElement
});

// Remember to call keyboardProvider.dispose() when the editor is closed or the feature is turned off.
*/
