if (typeof ($) === "function") {

    // Running for real, and jquery properly loaded first
    $(document).ready(function () {
        $('#showRecordingTools').change(function() {
            var page: HTMLIFrameElement = <HTMLIFrameElement>parent.window.document.getElementById('page');
            if (!page) return; // unit testing?
            if (this.checked) {
                page.contentWindow['startRecording']();
            } else {
                page.contentWindow['hideAudio']();
            }
        });
    });
}