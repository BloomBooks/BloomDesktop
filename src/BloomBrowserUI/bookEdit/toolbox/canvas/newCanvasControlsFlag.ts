export const kUseNewCanvasControlsStorageKey = "bloom-use-new-canvas-controls";

export const getUseNewCanvasControls = (): boolean => {
    if (typeof window === "undefined") {
        return false;
    }

    const search = new URLSearchParams(window.location.search);
    const queryValue = search.get("newCanvasControls");
    if (queryValue === "1" || queryValue === "true") {
        return true;
    }
    if (queryValue === "0" || queryValue === "false") {
        return false;
    }

    try {
        return (
            window.localStorage.getItem(kUseNewCanvasControlsStorageKey) ===
            "true"
        );
    } catch {
        return false;
    }
};
