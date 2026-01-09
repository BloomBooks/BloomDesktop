//Makes a toolbox off to the side (implemented using qtip), with elements that can be dragged
//onto the page.
//This was an experiment that, at least so far, we have not gone forward with. It was used by the "Template Maker" template.
//We could end up using this kind of UI someday, thus it is preserved in the code base for now.
import $ from "jquery";

function AddToolbox(container) {
    $(container)
        .find("div.bloom-page.bloom-enablePageCustomization")
        .each(function () {
            $(this)
                .find(".marginBox")
                .droppable({
                    hoverClass: "ui-state-hover",
                    accept: function () {
                        return true;
                    },
                    drop: function (event, ui) {
                        //is it being dragged in from a toolbox, or just moved around inside the page?
                        if (
                            $(ui.draggable).hasClass("widgetInPageDragToolbox")
                        ) {
                            //review: since we already did a clone during the tearoff, why clone again?
                            var $x = $($(ui.draggable).clone()[0]);
                            // $x.text("");

                            //we need different behavior when it is in the toolbox vs. once it is live
                            $x.attr("class", $x.data("classesafterdrop"));
                            $x.removeAttr("classesafterdrop");

                            if ($x.hasClass("bloom-canvas")) {
                                SetupBloomCanvas($x);
                            }

                            //review: this find() implies that the draggable thing isn't necesarily the widgetInPageDragToolbox. Why not?
                            //                    $(this).find('.widgetInPageDragToolbox')
                            //                            .removeAttr("style")
                            //                            .draggable({ containment: "parent" })
                            //                            .removeClass("widgetInPageDragToolbox")
                            //                            .SetupResizableElement(this)
                            //                            .SetupDeletable(this);
                            $x.removeAttr("style");
                            $x.draggable({ containment: "parent" });
                            $x.removeClass("widgetInPageDragToolbox");
                            SetupResizableElement($x);
                            SetupDeletable($x);

                            $(this).append($x);
                        }
                    },
                });
            var lang1Tag = GetSettings().languageForNewTextBoxes;
            var heading1CenteredWidget =
                '<div class="heading1-style centered widgetInPageDragToolbox"  data-classesafterdrop="bloom-translationGroup heading1-style centered bloom-resizable bloom-deletable bloom-draggable"><div data-classesafterdrop="bloom-editable bloom-content1" lang="' +
                lang1Tag +
                '">Heading 1 Centered</div></div>';
            var heading2LeftWidget =
                '<div class="heading2-style widgetInPageDragToolbox"  data-classesafterdrop="bloom-translationGroup heading2-style  bloom-resizable bloom-deletable bloom-draggable"><div data-classesafterdrop="bloom-editable bloom-content1" lang="' +
                lang1Tag +
                '">Heading 2, Left</div></div>';
            var fieldWidget =
                '<div class="widgetInPageDragToolbox" data-classesafterdrop="bloom-translationGroup bloom-resizable bloom-deletable bloom-draggable"><div data-classesafterdrop="bloom-editable bloom-content1" lang="' +
                lang1Tag +
                '"> A block of normal text.</div></div>';
            // old one: var imageWidget = '<div class="bloom-canvas bloom-resizable bloom-draggable  bloom-deletable widgetInPageDragToolbox"><img src="image-placeholder.png"></div>';
            var imageWidget =
                '<div class="widgetInPageDragToolbox " data-classesafterdrop="bloom-canvas  bloom-resizable bloom-draggable  bloom-deletable"><img src="image-placeholder.png"></div>';

            var toolbox = $(this)
                .parent()
                .append(
                    "<div id='pagedragtoolbox'><h3>Page Elements</h3><ul class='pagedragtoolbox'><li>" +
                        heading1CenteredWidget +
                        "</li><li>" +
                        heading2LeftWidget +
                        "</li><li>" +
                        fieldWidget +
                        "</li><li>" +
                        imageWidget +
                        "</li></ul></div>",
                );

            toolbox.find(".widgetInPageDragToolbox").each(function () {
                $(this).draggable({
                    //note: this is just used for drawing what you drag around..
                    //it isn't what the droppable is actually given. For that, look in the 'drop' item of the droppable() call above.
                    helper: function (event) {
                        var tearOff = $(this).clone(); //.removeClass('widgetInPageDragToolbox');//by removing this, we show it with the actual size it will be when dropped
                        return tearOff;
                    },
                });
            });
            $(this).qtipSecondary({
                content:
                    "<div id='experimentNotice'><img src='/bloom/images/experiment.png'/>This is an experimental prototype of template-making within Bloom itself. Much more work is needed before it is ready for real work, so don't bother reporting problems with it yet. The Trello board is <a href='https://trello.com/board/bloom-custom-template-dev/4fb2501b34909fbe417a7b7d'>here</a></b></div>",
                show: { ready: true },
                hide: false,
                position: {
                    at: "right top",
                    my: "left top",
                },
                style: {
                    classes: "ui-tooltip-red",
                    tip: { corner: false },
                },
            });
        });
}
