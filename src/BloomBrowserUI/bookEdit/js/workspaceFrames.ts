/* The Bloom "Edit"" pane currently works by having an outer window/html (once private to edit pane, now
        the root of the workspace) with a number of iframes.
        For better or worse, these iframes currently communicate with each other.
        These functions allow any of the iframes or the root to find any of the others. Each of these
        has an "entry point" javascript which is a file bundled by vite and <script>-included by the
        the html of that frame.
        In order to make the contents of that bundle and the context of that frame accessible from the
        outside, code sets a variable on the window object of each frame which exposes the
        functions in that frame that can be called from elsewhere.

        So this module just hides all that and allows code in any frame to access the exports on any other frame.
        Not to make it simpler (because it's already simple... see how few lines are here...) but in order
        to hide the details so that we can easily change it later.
*/

import type { IPageFrameExports } from "../editablePage";
import type { IWorkspaceExports } from "../workspaceRoot";
import type { IToolboxFrameExports } from "../toolbox/toolboxBootstrap";

export function getToolboxBundleExports(): IToolboxFrameExports | null {
    const frameWindow = getFrame("toolbox") as
        | (Window & { [key: string]: unknown })
        | null;
    return (
        (frameWindow?.["toolboxBundle"] as unknown as IToolboxFrameExports) ??
        null
    );
}
export function getEditablePageBundleExports(): IPageFrameExports | null {
    const frameWindow = getFrame("page") as
        | (Window & { [key: string]: unknown })
        | null;
    return (
        (frameWindow?.["editablePageBundle"] as unknown as IPageFrameExports) ??
        null
    );
}
export function getWorkspaceBundleExports(): IWorkspaceExports {
    const rootWindow = getRootWindow() as Window & { [key: string]: unknown };
    const workspaceBundle = rootWindow["workspaceBundle"];
    if (!workspaceBundle) {
        // Tempting to do an alert here. But if the browser control has not yet been added
        // to its parent, we won't see it, and the loading code will be frozen waiting for
        // a response to the alert. Hopefully the error will show up somewhere.
        throw new Error(
            "no workspace bundle exports! Did editing code get compiled into the wrong bundle?",
        );
    }
    return workspaceBundle as IWorkspaceExports;
}

// Like getWorkspaceBundleExports(), but returns null instead of throwing when there is no workspace
// bundle. Use this from code that can legitimately run with no surrounding workspace frame, e.g. the
// off-screen process-book context, where a page is loaded standalone with no parent workspace root.
export function tryGetWorkspaceBundleExports(): IWorkspaceExports | null {
    const rootWindow = getRootWindow() as Window & { [key: string]: unknown };
    return (rootWindow["workspaceBundle"] as IWorkspaceExports) ?? null;
}

// Do this task when the workspace bundle is loaded. If it isn't loaded already, we set a timeout and do it when we can.
// There is a similar doWhenToolboxLoaded in workspaceRoot.ts.
export function doWhenWorkspaceBundleLoaded(
    task: (workspaceExports: IWorkspaceExports) => unknown,
): void {
    const rootWindow = getRootWindow() as Window & { [key: string]: unknown };
    const bundle = rootWindow.workspaceBundle as IWorkspaceExports;
    if (bundle) {
        task(bundle);
        return;
    }

    window.setTimeout(() => {
        doWhenWorkspaceBundleLoaded(task);
    }, 10);
}

function getRootWindow(): Window {
    //if parent is null, we're the root
    return window.parent || window;
}

function getFrame(id: string): Window | null {
    const element = getRootWindow().document.getElementById(id);
    if (!element) {
        return null;
    }

    return (<HTMLIFrameElement>element).contentWindow;
}
