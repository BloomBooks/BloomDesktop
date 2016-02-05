import axios = require('axios');
if (typeof ($) === "function") { // have jquery
    $(document).ready(() => {
        // request our model and set the controls
        axios.get<string>('/bloom/bookSettings', result => {
            var settings = JSON.parse(result.data);
            // enhance: this is just dirt-poor binding of 1 checkbox for now
            $("input[name='unlockShellBook']").prop("checked", settings.unlockShellBook);
        });
    });
}

function handleBookSettingCheckboxClick(clickedButton: any) {
    // read our controls and send the model back to c#
    // enhance: this is just dirt-poor serialization of checkboxes for now
    var inputs = $(".bookSettings :input");
    var settings = $.map(inputs, function (input, i) {
        var o = {};
        o[input.name] = $(input).prop("checked");
        return o;
    })[0];
    bookSettingsFireCSharpEvent("setBookSettings", JSON.stringify(settings));
}

function bookSettingsFireCSharpEvent(eventName, eventData): void {
    var event = new MessageEvent(eventName, { 'bubbles': true, 'cancelable': true, 'data': eventData });
    document.dispatchEvent(event);
}