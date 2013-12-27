jQuery(document).ready(function () {

    $('ul').sortable({
       items: "li:not(.fixedPosition)"
    });
    $('ul').selectable({

    });
});