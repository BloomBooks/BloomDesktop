///<reference path="../../../typings/axios/axios.d.ts"/>
import axios = require('axios');
import * as JQuery from 'jquery';
import * as $ from 'jquery';

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

        window.setTimeout(loadPageOptions, 1000);
    });

});

function getPageFrame(): HTMLIFrameElement {
    return <HTMLIFrameElement>parent.window.document.getElementById('page');
}

// The body of the editable page, a root for searching for document content.
function getPage(): JQuery {
    var page = getPageFrame();
    if (!page) return null;
    return $(page.contentWindow.document.body);
}

function loadPageOptions(){
    var page = getPage().find('.bloom-page')[0];
    var initialOptions = page.getAttribute("data-page-layout-options") || "";

     $(page).find('.pageLayoutOptions div').each( (index, element) => {

         var localCopy = $(element).clone(false);

        //load the checkboxes according to the current value of the page's data-page-layout-options
        const key = $(localCopy).data('option');
        const checkbox:HTMLInputElement = <HTMLInputElement>$(localCopy).find('input')[0];
        checkbox.checked = initialOptions.indexOf(key) > -1;

        //when the user clicks on something, update the page's data
        $(localCopy).click((event) => {

            const page = getPage().find('.bloom-page')[0];
            let currentOptions = page.getAttribute("data-page-layout-options") || "";
            currentOptions = currentOptions.replace(key,"").trim();

            if(checkbox.checked) {
                currentOptions += " "+ key;
            }
            page.setAttribute("data-page-layout-options", currentOptions);
        });

        $('.pageLayoutOptions').append(localCopy);
    })
}

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

