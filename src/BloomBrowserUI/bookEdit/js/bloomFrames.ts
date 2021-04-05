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
export function getToolboxFrameExports(): IToolboxFrameExports | null {
    return getFrameExports("toolbox") as IToolboxFrameExports | null;
}
export function getPageFrameExports(): IPageFrameExports | null {
    return getFrameExports("page") as IPageFrameExports | null;
}
export function getEditViewFrameExports(): IEditViewFrameExports {
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

function getFrameExports(id: string): any {
    return getFrame(id)?.editTabBundle;
}
