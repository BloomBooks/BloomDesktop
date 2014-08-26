jQuery(document).ready(function() {

//    $('ul#pageThumbnails').sortable();
//    $('ul#pageThumbnails').disableSelection();

    $('.gridster ul').gridster({
        max_cols: 2,
        widget_base_dimensions: [60, 95],
        widget_margins: [5, 5]
    }).data('gridster');

});