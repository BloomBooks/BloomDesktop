jQuery(document).ready(function() {

//    $('ul#pageThumbnails').sortable();
//    $('ul#pageThumbnails').disableSelection();

    $('.gridly').gridly({
        base:60, // px
//        gutter: 5, // px
        columns: 4,
   draggable: {
        selector: '.movable'
      }
    });
});