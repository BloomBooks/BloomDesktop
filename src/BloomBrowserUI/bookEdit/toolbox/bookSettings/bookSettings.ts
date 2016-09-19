///<reference path="../../../typings/axios/axios.d.ts"/>
import axios = require('axios');

$(document).ready(() => {
    // request our model and set the controls
    axios.get<any>('/bloom/api/bookSettings').then(result => {
        var settings = result.data;

        // Only show this if we are editing a shell book. Otherwise, it's already not locked.
        if(!settings.isRecordedAsLockedDown){
            $(".showOnlyWhenBookWouldNormallyBeLocked").css("display","none");
        }
        else{
            // enhance: this is just dirt-poor binding of 1 checkbox for now
            $("input[name='unlockShellBook']").prop("checked", settings.unlockShellBook);
        }
    });
});

export function handleBookSettingCheckboxClick(clickedButton: any) {
    // read our controls and send the model back to c#
    // enhance: this is just dirt-poor serialization of checkboxes for now
    var inputs = $(".bookSettings :input");
    var settings = $.map(inputs, (input, i) => {
        var o = {};
        o[input.name] = $(input).prop("checked");
        return o;
    })[0];
    axios.post("/bloom/api/bookSettings", settings);
}


export function handleResetZoom(clickedButton: any) {
    var pageDom = <HTMLIFrameElement>parent.window.document.getElementById('page');
    var pageBody = $(pageDom.contentWindow.document.body);
    $(pageBody).css('transform', 'scale(' + 1.0 + ',' + 1.0 + ')');
}