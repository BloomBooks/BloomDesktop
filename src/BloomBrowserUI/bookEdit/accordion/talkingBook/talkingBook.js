if (typeof ($) === "function") {
    // Running for real, and jquery properly loaded first
    $(document).ready(function () {
        // We attach to the whole accordion, not to the #showRecordingTools directly,
        // because if the talking books tab is initially disabled, even after the page is loaded
        // #showRecordingTools may not exist, so the change handler has nowhere to attach.
        // It therefore doesn't work until we change pages.
        $('#accordion').on('change', '#showRecordingTools', function () {
            var page = parent.window.document.getElementById('page');
            if (!page)
                return; // unit testing?
            if (this.checked) {
                page.contentWindow.showRecordingTools();
            }
            else {
                page.contentWindow.hideRecordingTools();
            }
        });
    });
}
// Wish this was also called showRecordingTools. But the compiler claims it is a conflict with the top-level
// function by that name in audioRecording.ts. I don't think both are loaded into the same frame, so it should
// be OK. But maybe it is safer this way anyway.
function showRecordingControls() {
    $('#showRecordingTools').get(0).checked = true;
    var page = parent.window.document.getElementById('page');
    if (!page)
        return; // unit testing?
    page.contentWindow.showRecordingTools();
}
//# sourceMappingURL=talkingBook.js.map