/* The Bloom "Edit"" pane currently works by having an outer window/html with a number of iframes.
        For better or worse, these iframes currently communicate with each other.
        These functions allow any of the iframes or the root to find any of the others. Each of these
        has an "entry point" javacript which is a file bundled by webpack and <script>-included by the
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

interface WindowWithExports extends Window {
    editTabBundle: any;
}
export function getToolboxBundleExports(): IToolboxFrameExports | null {
    return (getFrame("toolbox") as any)
        ?.toolboxBundle as IToolboxFrameExports | null;
}
export function getEditablePageBundleExports(): IPageFrameExports | null {
    return (getFrame("page") as any)
        ?.editablePageBundle as IPageFrameExports | null;
}
export function getEditTabBundleExports(): IEditViewFrameExports {
    const rootWindow = getRootWindow();
    if (!rootWindow["editTabBundle"]) {
        // Tempting to do an alert here. But if the browser control has not yet been added
        // to its parent, we won't see it, and the loading code will be frozen waiting for
        // a response to the alert. Hopefully the error will show up somewhere.
        throw new Error(
            "no editTabBundle! Did editing code get compiled into the wrong bundle?"
        );
    }
    return (<any>getRootWindow()).editTabBundle as IEditViewFrameExports;
}

function getRootWindow(): Window {
    //if parent is null, we're the root
    return window.parent || window;
}

function getFrame(id: string): WindowWithExports | null {
    const element = getRootWindow().document.getElementById(id);
    if (!element) {
        return null;
    }

    return (<HTMLIFrameElement>element).contentWindow as WindowWithExports;
}
