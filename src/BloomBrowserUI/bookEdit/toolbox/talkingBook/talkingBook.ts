if (typeof ($) === "function") {

    // Running for real, and jquery properly loaded first
    $(document).ready(function () {
        // We attach to the whole toolbox, not to the #showRecordingTools directly,
        // because if the talking books tab is initially disabled, even after the page is loaded
        // #showRecordingTools may not exist, so the change handler has nowhere to attach.
        // It therefore doesn't work until we change pages.
        $('#toolbox').on('change', '#showRecordingTools', function() {
            var page: HTMLIFrameElement = <HTMLIFrameElement>parent.window.document.getElementById('page');
            if (!page) return; // unit testing?
            if (this.checked) {
                (<any>page.contentWindow).showRecordingTools();
            } else {
                (<any>page.contentWindow).hideRecordingTools();
            }
        });
    });
}

// Wish showRecordingTools was also called showTalkingBookTool. But the compiler claims it is a conflict with the top-level
// function by that name in audioRecording.ts. I don't think both are loaded into the same frame, so it should
// be OK. But maybe it is safer this way anyway. Moreover, I think both methods will shortly go away when we move the
// recording controls into the talking book tool/panel itself.
function showTalkingBookTool() {
    (<HTMLInputElement>$('#showRecordingTools').get(0)).checked = true;
    var page: HTMLIFrameElement = <HTMLIFrameElement>parent.window.document.getElementById('page');
    if (!page) return; // unit testing?
    (<any>page.contentWindow).showRecordingTools();
}