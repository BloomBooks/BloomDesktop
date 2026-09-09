// Waiting for CKEditor to finish attaching to a text box.
//
// The page's setup attaches CKEditor to each text box after it has run the flow pass, and
// CKEditor's own initialization then rewrites the box from the content it read as it attached.
// Anything written to the box in between is lost.
//
// The passes that settle boxes on one page survive that: the next pass reads both boxes and
// settles them again. The cross-page work does not, because by then the other half of the text
// has gone to a box on another page and been saved there, and a second settle would send it
// twice. So the cross-page work waits for the box's editor first.

/** How long to wait before going ahead without an editor. A box can legitimately have none. */
const kMaxWaitMs = 2000;

type CkEditorInstance = { status?: string };
type CkEditorGlobal = {
    dom: {
        element: {
            get: (node: HTMLElement) => {
                getEditor?: (create?: boolean) => CkEditorInstance | undefined;
            };
        };
    };
};

/**
 * Settles once CKEditor has finished attaching to this box, or once it is clear that no editor
 * is coming. Returns immediately when CKEditor is not there at all, which is the case in tests.
 */
export async function waitForEditorReady(box: HTMLElement): Promise<void> {
    const ckeditor = (window as unknown as { CKEDITOR?: CkEditorGlobal })
        .CKEDITOR;
    if (!ckeditor?.dom?.element?.get) {
        return;
    }

    const readAt = Date.now();
    while (Date.now() - readAt < kMaxWaitMs) {
        if (!box.isConnected) {
            return;
        }

        // Ask for the editor of this element without making one: getEditor() creates an editor
        // when told to, and an editor of our own would take the box over.
        const editor = ckeditor.dom.element.get(box).getEditor?.(false);
        if (editor?.status === "ready") {
            return;
        }

        await nextFrame();
    }
}

function nextFrame(): Promise<void> {
    return new Promise((resolve) => {
        requestAnimationFrame(() => resolve());
    });
}
