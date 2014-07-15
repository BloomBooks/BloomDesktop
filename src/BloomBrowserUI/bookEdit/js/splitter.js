$(function() {

    // We stick a horizontalSplitter in between pairs of objects with the heightAdjustable class
    $('.heightAdjustable').each(function(){
        if($(this).next().hasClass('heightAdjustable')){
            $(this).after("<div class='horizontalSplitter'></div>");
        }
    });
    $('.bloom-translationGroup, .bloom-imageContainer').each(function(){
        var page = $(this).closest('.bloom-page')[0];
//      Limit to descendants of Basic Book: if(page.id.indexOf('5dcd48df')!=0 &&
//           $(page).data('pagelineage').indexOf('5dcd48df')!=0)
//            return;
        var previous = $(this).prev();
        if(previous.hasClass('bloom-translationGroup') || previous.hasClass('bloom-imageContainer')){
            $(this).before("<div class='horizontalSplitter bloom-ui'></div>");
        }
    });
    function Splitter(affordance, topElement, bottomElement){
        var lastY= 0;
        var self= this;

        this.topElement = topElement;
        this.bottomElement = bottomElement;

        affordance.addEventListener('mousedown', function(event) {
            event.preventDefault();    /* prevent teYt selection */

            self.lastY = event.clientY;

            window.addEventListener('mousemove', self.drag);
            window.addEventListener('mouseup', self.endDrag);
        });

        this.drag = function(event) {
            var topHeight;
            var bottomHeight;
            var verticalChange = event.clientY - self.lastY;
            console.log("vertical change= " + event.clientY + "-" + self.lastY + " = " + verticalChange);
            topHeight = $(self.topElement).height();
            bottomHeight = $(self.bottomElement).height();
            topHeight = parseInt(topHeight, 10) + verticalChange;
            bottomHeight = parseInt(bottomHeight, 10) - verticalChange;
            $(self.topElement).height(topHeight + 'px');
            var h = $(self.bottomElement).height();
            //$(self.bottomElement).height(bottomHeight + 'px');
            $(self.bottomElement).css('height',bottomHeight + 'px');
            console.log(h+"-->"+bottomHeight+" actual:"+$(self.bottomElement).height());
            self.lastY = event.clientY;
        };

        this.endDrag=function() {
            window.removeEventListener('mousemove', self.drag);
            window.removeEventListener('mouseup', self.endDrag);
        };
    }
    $('.horizontalSplitter').each(function(){
        new Splitter(this, $(this).prev(), $(this).next());
    });
});


//    Experiments in the next stage which would be allowing adding of new image and text blocks
// $('.addImage').click(function(){
//        var before = $(this).closest('.addOns')[0];
//         before = $(before).prev();
//        $(before).after("<div class='bloom-imageContainer'><img src='placeholder.png'></div>");
//    });
//    $('.addText').click(function(){
//        var before = $(this).closest('.addOns')[0];
//         before = $(before).prev();
//        $(before).after("<P>Hello World</P>");
//    });