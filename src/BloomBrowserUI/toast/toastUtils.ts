import * as React from "react";
import { ToastInfo } from "./Toast";

const kToastDebugShowEvent = "bloom-toast-debug-show";
const kToastDebugClearEvent = "bloom-toast-debug-clear";

export type ToastDebugInput = ToastInfo | ToastInfo[];

export const toastDebugEvents = {
    clear: kToastDebugClearEvent,
    show: kToastDebugShowEvent,
};

export const useToastDebugEvents = (
    enqueueToasts: (incomingToasts: ToastInfo[]) => void,
    clearToasts: () => void,
): void => {
    React.useEffect(() => {
        const handleDebugShow = (event: Event) => {
            const customEvent = event as CustomEvent<ToastDebugInput>;
            const requestedToasts = Array.isArray(customEvent.detail)
                ? customEvent.detail
                : [customEvent.detail];

            enqueueToasts(requestedToasts);
        };

        window.addEventListener(toastDebugEvents.show, handleDebugShow);
        window.addEventListener(toastDebugEvents.clear, clearToasts);

        return () => {
            window.removeEventListener(toastDebugEvents.show, handleDebugShow);
            window.removeEventListener(toastDebugEvents.clear, clearToasts);
        };
    }, [clearToasts, enqueueToasts]);
};
