/* The Bloom "Edit"" pane currently works by having an outer window/html with a number of iframes.
        For better or worse, these iframes currently communicate with each other.
        These functions allow any of the iframes or the root to find any of the others. Each of these
        has an "entry point" javascript which is a file bundled by webpack and <script>-included by the
        the html of that frame.
        In order to make the contents of that bundle and the context of that frame accessible from the
        outside, Webpack is set so that the first line of each of these "entry point" files
        is something like
        var editTabBundle = {.....}

        So this module just hides all that and allows code in any frame to access the exports on any other frame.
        Not to make it simpler (because it's already simple... see how few lines are here...) but in order
        to hide the details so that we can easily change it later.
*/

import { IPageFrameExports } from "../editablePage";
import { IEditViewFrameExports } from "../editViewFrame";
import { IToolboxFrameExports } from "../toolbox/toolboxBootstrap";

type WindowWithExports = Window & {
    editTabBundle?: unknown;
    toolboxBundle?: unknown;
    editablePageBundle?: unknown;
};
export function getToolboxBundleExports(): IToolboxFrameExports | null {
    const frame = getFrame("toolbox");
    return (frame?.toolboxBundle as IToolboxFrameExports | undefined) || null;
}
export function getEditablePageBundleExports(): IPageFrameExports | null {
    const frame = getFrame("page");
    return (frame?.editablePageBundle as IPageFrameExports | undefined) || null;
}
export function getEditTabBundleExports(): IEditViewFrameExports {
    const rootWindow = getRootWindow();
    const bundle = rootWindow.editTabBundle as
        | IEditViewFrameExports
        | undefined;
    if (!bundle) {
        // Tempting to do an alert here. But if the browser control has not yet been added
        // to its parent, we won't see it, and the loading code will be frozen waiting for
        // a response to the alert. Hopefully the error will show up somewhere.
        throw new Error(
            "no editTabBundle! Did editing code get compiled into the wrong bundle?",
        );
    }
    return bundle;
}

// Keep trying for roughly four seconds. We start with quick retries because we don't actually expect it
// to take long at all. But eventually use longer delays in case something unexpected is going on.
const editTabBundleRetryDelaysMs = [
    20, 30, 50, 75, 125, 200, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250,
    250, 250,
];
export function runWhenEditTabBundleAvailable(
    task: (bundle: IEditViewFrameExports) => void,
    onTimeout?: () => void,
    attempt = 0,
): void {
    const rootWindow = getRootWindow();
    if (rootWindow.editTabBundle) {
        task(rootWindow.editTabBundle as IEditViewFrameExports);
        return;
    }

    if (attempt >= editTabBundleRetryDelaysMs.length) {
        if (onTimeout) {
            onTimeout();
        } else {
            console.warn("editTabBundle not available after retries");
        }
        return;
    }

    const delay = editTabBundleRetryDelaysMs[attempt];
    window.setTimeout(() => {
        runWhenEditTabBundleAvailable(task, onTimeout, attempt + 1);
    }, delay);
}

function getRootWindow(): WindowWithExports {
    //if parent is null, we're the root
    return (window.parent || window) as WindowWithExports;
}

function getFrame(id: string): WindowWithExports | null {
    const element = getRootWindow().document.getElementById(id);
    if (!element) {
        return null;
    }

    const contentWindow = (<HTMLIFrameElement>element).contentWindow;
    return contentWindow ? (contentWindow as WindowWithExports) : null;
}
