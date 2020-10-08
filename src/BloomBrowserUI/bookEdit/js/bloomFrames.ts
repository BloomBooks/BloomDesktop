/* The Bloom "Edit"" pane currently works by having an outer window/html with a number of iframes.
        For better or worse, these iframes currently communicate with each other.
        These functions allow any of the iframes or the root to find any of the others. Each of these
        has an "entry point" javacript which is a file bundled by webpack and <script>-included by the
        the html of that frame.
        In order to make the contents of that bundle and the context of that frame accessible from the
        outside, Webpack is set so that the first line of each of these "entry point" files
        is something like
        var FrameExports = {.....}

        So this module just hides all that and allows code in any frame to access the exports on any other frame.
        Not to make it simpler (because it's already simple... see how few lines are here...) but in order
        to hide the details so that we can easily change it later.
*/

interface WindowWithExports extends Window {
    FrameExports: any;
}
export function getToolboxFrameExports() {
    return getFrameExports("toolbox");
}

export function getTheOneToolboxThen(doThis: (x: any) => void) {
    const frameExports = getToolboxFrameExports();
    if (frameExports) {
        const theOneToolbox = frameExports.getTheOneToolbox();
        if (theOneToolbox) {
            doThis(theOneToolbox);
            return;
        }
    }
    // If the toolbox isn't ready yet, try later.
    setTimeout(() => getTheOneToolboxThen(doThis), 200);
}
export function getPageFrameExports(): any | undefined {
    return getFrameExports("page");
}
export function getEditViewFrameExports() {
    return (<any>getRootWindow()).FrameExports;
}

function getRootWindow(): Window {
    //if parent is null, we're the root
    return window.parent || window;
}
function getFrame(id: string): WindowWithExports | undefined {
    // Enhance: This needs a plan for what happens if getElementById returns null.
    const iframe = <HTMLIFrameElement>(
        getRootWindow().document.getElementById(id)
    );
    if (iframe) {
        return iframe.contentWindow as WindowWithExports;
    }
    return undefined;
}
function getFrameExports(id: string): any | undefined {
    const frame = getFrame(id);
    return frame ? frame.FrameExports : undefined;
}
