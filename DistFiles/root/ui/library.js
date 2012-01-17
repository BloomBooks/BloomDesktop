function SelectBook(book)
{
    //$(".selectedBook").removeClass("selectedBook");
    //$(book).addClass("selectedBook");
}

jQuery(function () {
    //$("img").disableSelection();
    $("ul.library").sortable( {
        receive: function(event, ui){
            $(".selectedBook").removeClass("selectedBook");
//            ui.helper.removeClass();
//            ui.helper.addClass("selectedBook");
            var x = $(this).find(".ui-draggable");
            $(x).removeClass();
            $(x).addClass("bookLI selectedBook");
            $(x).removeAttr("style");
        }
    });
    $("ul.collection > li").draggable({//.draggable({
        connectToSortable: 'ul.library',
        helper: "clone",
        appendTo: 'div#libraryContainer',
					revert: "invalid"
       , stop: function(event, ui) {SelectBook(ui.helper)}
    });
    $("ul, li").disableSelection();
//    $("li.vernacularBook").mouseenter(new function () {
//
//        $(this).addClass("selectedBook");
//
//    });
//    $("li.vernacularBook").click(new function () {
//
//        $(this).addClass("selectedBook");
//    });


    // Select all elements that are to share the same tooltip
      var elems = $('li.bookLI')

//      // Store our title attribute and remove it so we don't get browser tooltips showing up
//      elems.each(function(i) {
//         $.attr(this, 'tooltip', $.attr(this, 'title'));
//      })
//      .removeAttr('title');

      // Create the tooltip on a dummy div since we're sharing it between targets
      $('<div />').qtip(
      {
         content:  $("div#collectionItemPopup"),
         position: {
            target: 'event', // Use the triggering element as the positioning target
            effect: false,   // Disable default 'slide' positioning animation

                my: 'top left',
                at: 'top right'

         },
         show: {
            target: elems
         },
         hide: {
            target: elems
         },
         events: {
            show: function(event, api) {
               // Update the content of the tooltip on each show
               var target = $(event.originalEvent.target);

               if(target.length) {
                  //api.set('wrapper', $("div#collectionItemPopup"));
                   $("div#collectionItemPopup div.bookTitle").text($(target).find("div.bookTitle").text());
                   $("div#collectionItemPopup div.abstract").text($(target).find("div.abstract").text());               }
            }
         },
          style: {
                tip: false
             }
      });
//
//        $("UL.collection li").qtip({
//            content: $("DIV#collectionItemPopup"),
//            position: {
//               my: 'top left',
//               at: 'top right'
//            },
//            style: {
//                  tip: false
//               }
//        });



});