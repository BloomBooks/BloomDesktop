if (typeof ($) === "function") {
    // Running for real, and jquery properly loaded first
    $(document).ready(function () {
        $('#showRecordingTools').change(function () {
            var page = parent.window.document.getElementById('page');
            if (!page)
                return; // unit testing?
            if (this.checked) {
                page.contentWindow['startRecording']();
            }
            else {
                page.contentWindow['hideAudio']();
            }
        });
    });
}
//# sourceMappingURL=talkingBook.js.map