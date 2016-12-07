/**
        MyImgScale v0.2

        MyImgScale is a jQuery plugin to scale images to fit or fill their parent container.
        Note: The parent container of the image must have a defined height and width in CSS.

        It is actually a merger/improvement from two existing plugins:
         1) Image Scale v1.0 by Kelly Meath (http://imgscale.kjmeath.com/), and
         2) CJ Object Scaler v3.0.0 by Doug Jones (http://www.cjboco.com/projects.cfm/project/cj-object-scaler/)

        The reasons for this merger are:
        . CJ Object Scaler has a conciser image resizing algorithm while Image Scale has a clearer layout.
        . CJ Object Scaler has an overflow issue, ie. the image scaled is not confined in parent container.
        . Both have the wrong calculation when parent container is invisible.

        If the parent container has padding, the scaled image might still cross boundary.
        One of the solutions is to insert a wrapper div with the same height and width as the parent container, eg:
        <div id="parent" style="height: 120px; width: 90px; padding: 10px">
            <div id="interimWrapper" style="height: 120px; width: 90px;">
                <img src="<Your img link here>" />
            </div>
        </div>
        I prefer to do this in application rather than in plugin as it is somewhat obtrusive.

        Web: https://bitbucket.org/marshalking/myimgscale
        Updated: Apr 15, 2011 by Marshal

        -----------------------------------------------------------------------
        MIT License

        Copyright (c) 2011 Doug Jones, Kelly Meath, Marshal

        Permission is hereby granted, free of charge, to any person obtaining
        a copy of this software and associated documentation files (the
        "Software"), to deal in the Software without restriction, including
        without limitation the rights to use, copy, modify, merge, publish,
        distribute, sublicense, and/or sell copies of the Software, and to
        permit persons to whom the Software is furnished to do so, subject to
        the following conditions:

        The above copyright notice and this permission notice shall be
        included in all copies or substantial portions of the Software.

        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
        EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
        MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
        NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
        LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
        OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
        WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

(function($) {
        $.fn.scaleImage = function(options) {
                var opts = $.extend({parent: false, scale: 'fill', center: true, fade: 0}, options); // merge default options with user's

                return this.each(function() {
                        var $img = $(this);
                        var $parent = opts.parent ? $img.parents(opts.parent) : $img.parent(); // if not supplied, use default direct parent
                        //hatton removed this... leave it to the stylesheet: $parent.css({overflow: 'hidden'}); // keep the img inside boundaries

                        if ($parent.length > 0) {
                                $img.removeAttr('height').removeAttr('width');
                                if (this.complete) { // img already loaded/cached
                                        scale($img, $parent);
                                } else {
                                        $img.load(function() {
                                                scale($img, $parent);
                                        });
                                }
                        }
                });

                function scale($img, $parent) {

                        $img.removeAttr("style");//hatton added. Else, change of options isn't always going to be reflected, as the old styles stick around

                        var imgSize = getOriginalImgSize($img),
                                imgW = imgSize.width,
                                imgH = imgSize.height,
                                destW = $parent.width(),
                                destH = $parent.height(),
                                borderW = parseInt($img.css("borderLeftWidth"), 10) + parseInt($img.css("borderRightWidth"), 10),
                                borderH = parseInt($img.css("borderTopWidth"), 10) + parseInt($img.css("borderBottomWidth"), 10),
                                ratioX, ratioY, ratio, newWidth, newHeight;

                        if (destH === 0 || destW === 0) { // parent is invisible, eg. display: none
                                var parentSize = getHiddenElemSize($parent);
                                destW = parentSize.width;
                                destH = parentSize.height;
                        }

                        // check for valid border values. IE takes in account border size when calculating width/height so just set to 0
                        borderW = isNaN(borderW) ? 0 : borderW;
                        borderH = isNaN(borderH) ? 0 : borderH;

                        // calculate scale ratios
                        ratioX = destW / imgW;
                        ratioY = destH / imgH;

                        // Determine which algorithm to use
                        if (opts.scale === "fit") {
                                ratio = ratioX < ratioY ? ratioX : ratioY;
                        } else if (opts.scale === "fill") {
                                ratio = ratioX > ratioY ? ratioX : ratioY;
                        }

                        // calculate our new image dimensions
                        newWidth = parseInt(imgW * ratio, 10) - borderW;
                        newHeight = parseInt(imgH * ratio, 10) - borderH;

                        // Set new dimensions to both css and img's attributes
                        $img.css({
                                "width": newWidth,
                                "height": newHeight
                        }).attr({
                                "width": newWidth,
                                "height": newHeight
                        });

                        if (opts.center) { // set offset if center is true
                                $img.css("margin-left", Math.floor((destW - newWidth) / 2));
                                $img.css("margin-top", Math.floor((destH - newHeight) / 2));
                        }
//
//       hatton: leave opacity to the stylesheet     if (opts.fade > 0) { // fade-in effect
//                $parent.animate({opacity: 1}, opts.fade);
//            } else {
//                $parent.css("opacity", 1);
//            }
                }

                /**
                 * To calculate the correct scale ratio, we need the image's original size rather than some preset values,
                 * which were set either manually in code or automatically by browser.
                 * Thanks FDisk for the solution:
                 * http://stackoverflow.com/questions/318630/get-real-image-width-and-height-with-javascript-in-safari-chrome
                 */
                function getOriginalImgSize($img) {
                        var t = new Image();
                        t.src = $img.attr("src");
                        return {width: t.width, height: t.height};
                }

                /**
                 * If the element is invisible, jQeury .height() and .width() return 0 in IE.
                 * This function returns the hidden element's correct width and height.
                 * Thanks elliotlarson for the solution:
                 * http://stackoverflow.com/questions/2345784/jquery-get-height-of-hidden-element-in-jquery-1-4-2
                 */
                function getHiddenElemSize(element) {
                        var copy = element.clone().css({visibility: 'hidden', display: 'block', position: 'absolute'});
                        $("body").append(copy);
                        var size = {width: copy.width(), height: copy.height()};
                        copy.remove();
                        return size;
                }
        };
})(jQuery);
