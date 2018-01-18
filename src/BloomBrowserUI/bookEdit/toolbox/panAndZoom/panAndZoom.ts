// This class supports specifying Pan/Zoom animation

import * as JQuery from 'jquery';
import * as $ from 'jquery';
import { ITabModel } from "../toolbox";
import { ToolBox } from "../toolbox";
import { EditableDivUtils } from "../../js/editableDivUtils";
import { getPageFrameExports } from '../../js/bloomFrames';
import AudioRecording from '../talkingBook/audioRecording';


export default class PanAndZoom implements ITabModel {
    beginRestoreSettings(settings: string): JQueryPromise<void> {
        // Nothing to do, so return an already-resolved promise.
        var result = $.Deferred<void>();
        result.resolve();
        return result;
    }
    configureElements(container: HTMLElement) {
    }
    isAlwaysEnabled(): boolean {
        return false;
    }
    showTool() {
        $("input[name='panAndZoom']").change(() => this.zoomAndPanChanged());
        $('#panAndZoom-play-wrapper').click(() => this.previewPanAndZoom())
        this.updateMarkup();
    }
    hideTool() {
        const page = this.getPage();
        page.find('#animationStart').remove();
        page.find('#animationEnd').remove();
        // enhance: if more than one image...do what??
        const firstImage = page.find(".bloom-imageContainer").first();
        if (!firstImage.length) {
            return;
        }
        firstImage.removeClass("bloom-hideImageButtons");
        page.find('.ui-audioCurrent').removeClass('ui-audioCurrent');
    }
    updateMarkup() {
        const page = this.getPage();
        // enhance: if more than one image...do what??
        const firstImage = page.find(".bloom-imageContainer").first();
        $("#panAndZoom-choose-img").hide();
        if (!firstImage.length || firstImage.attr('data-disabled-initialrect')) {
            $("input[name='panAndZoom']").prop("checked", false);
            return; // enhance: possibly give some indication why we can't animate?
        }
        if (firstImage.find("img").attr("src") === "placeHolder.png") {
            $("#panAndZoom-choose-img").show();
            new MutationObserver(() => this.updateMarkup()).observe(firstImage.find("img")[0], { attributes: true, attributeFilter: ["src"] })
            return;
        }
        $("input[name='panAndZoom']").prop("checked", true);
        // enhance: should we do something special to keep the image-setting buttons available
        // when the image is our placeholder?
        firstImage.addClass("bloom-hideImageButtons");
        page.find('#animationStart').remove();
        page.find('#animationEnd').remove();
        const scale = EditableDivUtils.getPageScale();
        const imageHeight = firstImage.height();
        const imageWidth = firstImage.width();
        let usingDefaults = false;
        const makeResizeRect = (handleLabel: string, id: string, left: number, top: number, width: number, height: number, initAttr: string): JQuery => {
            const savedState = firstImage.attr(initAttr);
            if (savedState) {
                try {
                    const parts = savedState.split(" ");
                    left = parseFloat(parts[0]) * imageWidth;
                    top = parseFloat(parts[1]) * imageHeight;
                    width = parseFloat(parts[2]) * imageWidth;
                    height = parseFloat(parts[3]) * imageHeight;
                }
                catch (e) {
                    // If there's a problem with saved state, just go back to defaults.
                    usingDefaults = true;
                }
            } else {
                usingDefaults = true;
            }
            // So far, I can't figure out what the 3000 puts us in front of, but we have to be in front of it for dragging to work.
            // ui-resizable styles are setting font-size to 0.1px. so we have to set it back.
            const htmlForHandle = "<div id='elementId' class='classes' style='width:30px;height:30px;background-color:black;color:white;z-index:3000;cursor:default;'><p style='padding: 2px 0px 0px 9px;font-size:16px'>" + handleLabel + "</p></div>";
            const htmlForDragHandle = htmlForHandle.replace("elementId", "dragHandle").replace("classes", "bloom-dragHandleAnimation");
            const htmlForResizeHandles = htmlForHandle.replace("elementId", "resizeHandle")
                .replace("classes", "ui-resizable-handle ui-resizable-se") // the "2 box in the lower right"
                + "<div class='ui-resizable-handle ui-resizable-e' style='z-index: 90;'></div>" // handle on left edge (standard appearance)
                + "<div class='ui-resizable-handle ui-resizable-s' style='z-index: 90;'></div>" // handle on bottom edge (standard appearance)
                + "<div class='ui-resizable-handle ui-resizable-w' style='z-index: 90;'></div>" // handle on right edge (standard appearance)
                + "<div class='ui-resizable-handle ui-resizable-n' style='z-index: 90;'></div>"; // handle on top edge (standard appearance)
            // The order is important here. Something assumes the drag handle is the first child.
            // It does not work well to just put the border on the outer div.
            // - without a z-index it somehow disappears over jpegs.
            // - putting a z-index on the actual draggable makes a new stacking context and somehow messes up how draggable/resizable work.
            //      All kinds of weird things happen, like handles disappearing, or not being able to click on them when one box is inside the other.
            const htmlForDraggable = "<div id='" + id + "' style='height: " + height + "px; width:" + width + "px; position: absolute; top: " + top + "px; left:" + left + "px;'>"
                + htmlForDragHandle
                + "  <div style='height:100%;width:100%;position:absolute;top:0;left:0;border: dashed black 2px;box-sizing:border-box;z-index:1'></div>"
                + htmlForResizeHandles
                + "</div>";
            // Do NOT use an opacity setting here. Besides the fact that there's no reason to dim the rectangle while
            // moving it, it makes a new stacking context, and somehow this causes the dragged rectangle to go
            // behind jpeg images.
            const argsForDraggable = {
                handle: ".bloom-dragHandleAnimation", containment: "parent",
                stop: (event, ui) => this.updateDataAttributes(),
                // This bizarre kludge works around a bug that jquery have decided not to fix in their jquery draggable
                // code (https://bugs.jqueryui.com/ticket/6844). Basically without this the dragging happens as if
                // the view were not scaled. In particular there's a sudden jump when dragging starts.
                drag: (event, ui) => {
                    const xpos = ui.position.left / scale;
                    const ypos = ui.position.top / scale;
                    ui.position.top = ypos;
                    ui.position.left = xpos;
                }
            };
            const argsForResizable = {
                handles: { e: '.ui-resizable-e', s: '.ui-resizable-s', n: '.ui-resizable-n', w: '.ui-resizable-w', se: "#resizeHandle" },
                containment: "parent", aspectRatio: true,
                stop: (event, ui) => this.updateDataAttributes()
            };
            // Unless the element is created and made draggable and resizable in the page iframe's execution context,
            // the dragging and resizing just don't work.
            return getPageFrameExports().makeElement(htmlForDraggable, firstImage, argsForResizable, argsForDraggable);
        };
        const rect1 = makeResizeRect("1", "animationStart", 2, 2, imageWidth * 3 / 4, imageHeight * 3 / 4, "data-initialrect");
        const rect2 = makeResizeRect("2", "animationEnd", imageWidth * 3 / 8, imageHeight / 8, imageWidth / 2, imageHeight / 2, "data-finalrect");
        if (usingDefaults) {
            // If we're using defaults, the current rectangle positions don't correspond to what's in the file
            // (typically because we're animating this picture for the first time).
            // In case the user is happy with the defaults and doesn't move anything, we should save them.
            // If not using defaults, we don't want to change the file, because some pixel approximation
            // is involved in reading the rectangle positions, and we don't want the rectangles to creep
            // a fraction of a pixel each time we open this page.
            this.updateDataAttributes();
        }
    }
    name(): string {
        return 'panAndZoom';
    }
    hasRestoredSettings: boolean;
    finishTabPaneLocalization(pane: HTMLElement) {
    }

    zoomAndPanChanged() {
        const page = this.getPage();
        const firstImage = page.find(".bloom-imageContainer").first();
        if (!firstImage.length) {
            // Just turn it back off if there's nothing to animate
            $("input[name='panAndZoom']").prop("checked", false);
            return;
        }
        const zoomAndPanEnabled = $("input[name='panAndZoom']").prop("checked");
        if (zoomAndPanEnabled) {
            if (!firstImage.attr("data-initialrect")) {
                // see if we can restore a backup state
                firstImage.attr("data-initialrect", firstImage.attr("data-disabled-initialrect"));
                firstImage.removeAttr("data-disabled-initialrect");
                this.updateMarkup();
            }
        } else {
            if (firstImage.attr("data-initialrect")) { // always?
                // save old state, thus recording that we're in the off state, not just uninitialized.
                firstImage.attr("data-disabled-initialrect", firstImage.attr("data-initialrect"));
                firstImage.removeAttr("data-initialrect");
            }
            this.hideTool();
        }
    }

    updateDataAttributes(): void {
        const page = this.getPage();
        const startRect = page.find('#animationStart');
        const endRect = page.find('#animationEnd');
        const image = startRect.parent();

        const fullHeight = image.height();
        const fullWidth = image.width();

        const scale = EditableDivUtils.getPageScale();

        image.attr("data-initialrect", "" + startRect.position().left / fullWidth / scale + " " + startRect.position().top / fullHeight / scale
            + " " + startRect.width() / fullWidth + " " + startRect.height() / fullHeight);
        image.attr("data-finalrect", "" + endRect.position().left / fullWidth / scale + " " + endRect.position().top / fullHeight / scale
            + " " + endRect.width() / fullWidth + " " + endRect.height() / fullHeight);
    }

    private wrapperClassName = "bloom-ui-animationWrapper";

    // This code shares various aspects with BloomPlayer. But I don't see a good way to share them, and many aspects are very different.
    // - This code is simpler because there is only ever one pan-and-zoom-animation-capable image in a document (for now, anyway, since bloom only
    // displays one page at a time in edit mode and we only support pan and zoom on the first image)
    // - this code is also simpler because we don't have to worry about the image not yet being loaded by the time we
    // want to set up the animation
    // - this code is complicated by having to deal with problems caused by parent divs using scale for zoom.
    // somewhat more care is needed here to avoid adding the animation stuff permanently to the document
    previewPanAndZoom() {
        const page = this.getPage();
        const pageDoc = this.getPageFrame().contentWindow.document;
        const firstImage = page.find(".bloom-imageContainer").first();
        if (!firstImage.length || !$("input[name='panAndZoom']").prop("checked")) {
            return;
        }
        var duration = 0;
        $(page).find(".bloom-editable.bloom-content1").find("span.audio-sentence").each((index, span) => {
            var spanDuration = parseFloat($(span).attr("data-duration"));
            if (spanDuration) { // might be NaN if missing or somehow messed up
                duration += spanDuration;
            }
        })
        if (duration < 0.5) {
            duration = 4;
        }
        const scale = EditableDivUtils.getPageScale();
        // Make a div that wraps a div that will move and be clipped which wraps a clone of firstImage
        const wrapDiv = getPageFrameExports().makeElement("<div class='" + this.wrapperClassName + " bloom-animationWrapper' style='visibility: hidden; background-color:white; "
            + "height:" + firstImage.height() * scale + "px; width:" + firstImage.width() * scale + "px; "
            + "position: absolute;"
            + "left:" + firstImage[0].getBoundingClientRect().left + "px; "
            + "top: " + firstImage[0].getBoundingClientRect().top + "px; "
            + "'><div id='bloom-movingDiv'></div></div>", $(pageDoc.body));
        const movingDiv = wrapDiv.find("#bloom-movingDiv");
        var picToAnimate = firstImage.clone(true);
        picToAnimate.find('#animationStart').remove();
        picToAnimate.find('#animationEnd').remove();
        movingDiv.append(picToAnimate);
        page.append(wrapDiv);
        // Todo: it needs to be the page's document.
        const animationElement = pageDoc.createElement("style");
        animationElement.setAttribute("type", "text/css");
        animationElement.setAttribute("id", "animationSheet");
        animationElement.innerText = ".bloom-ui-animationWrapper {overflow: hidden; translateZ(0)} "
            + ".bloom-animate {height: 100%; width: 100%; "
            + "background-repeat: no-repeat; background-size: contain}";
        pageDoc.body.appendChild(animationElement);
        const stylesheet = animationElement.sheet;
        const initialRectStr = firstImage.attr("data-initialrect");
        const initialRect = initialRectStr.split(" ");
        const initialScaleWidth = 1 / parseFloat(initialRect[2]) * scale;
        const initialScaleHeight = 1 / parseFloat(initialRect[3]) * scale;
        const finalRectStr = firstImage.attr("data-finalrect");
        const finalRect = finalRectStr.split(" ");
        const finalScaleWidth = 1 / parseFloat(finalRect[2]) * scale;
        const finalScaleHeight = 1 / parseFloat(finalRect[3]) * scale;
        const wrapDivWidth = wrapDiv.width();
        const wrapDivHeight = wrapDiv.height();
        const initialX = parseFloat(initialRect[0]) * wrapDivWidth / scale;
        const initialY = parseFloat(initialRect[1]) * wrapDivHeight / scale;
        const finalX = parseFloat(finalRect[0]) * wrapDivWidth / scale;
        const finalY = parseFloat(finalRect[1]) * wrapDivHeight / scale;
        const animateStyleName = "bloom-animationPreview";
        const movePicName = "movepic";
        // Will take the form of "scale3d(W, H,1.0) translate3d(Xpx, Ypx, 0px)"
        // Using 3d scale and transform apparently causes GPU to be used and improves
        // performance over scale/transform. (https://www.kirupa.com/html5/ken_burns_effect_css.htm)
        // May also help with blurring of material originally hidden.
        const initialTransform = "scale3d(" + initialScaleWidth + ", " + initialScaleHeight
            + ", 1.0) translate3d(-" + initialX + "px, -" + initialY + "px, 0px)";
        const finalTransform = "scale3d(" + finalScaleWidth + ", " + finalScaleHeight
            + ", 1.0) translate3d(-" + finalX + "px, -" + finalY + "px, 0px)";
        //Insert the keyframe animation rule with the dynamic begin and end set
        (<CSSStyleSheet>stylesheet).insertRule("@keyframes " + movePicName
            + " { from{ transform-origin: 0px 0px; transform: " + initialTransform
            + "; } to{ transform-origin: 0px 0px; transform: " + finalTransform + "; } }", 0);

        //Insert the css for the imageView div that utilizes the newly created animation
        // We make the animation longer than the narration by the transition time so
        // the old animation continues during the fade.
        (<CSSStyleSheet>stylesheet).insertRule("." + animateStyleName
            + " { transform-origin: 0px 0px; transform: "
            + initialTransform
            + "; animation-name: " + movePicName + "; animation-duration: "
            + duration
            + "s; animation-fill-mode: forwards; "
            + "animation-timing-function: linear;}", 1);

        movingDiv.attr("class", "bloom-animate bloom-pausable " + animateStyleName);
        // At this point the wrapDiv becomes visible and the animation starts.
        //wrapDiv.show(); mysteriously fails
        wrapDiv.attr("style", wrapDiv.attr("style").replace("visibility: hidden; ", ""));
        // Play the audio during animation
        const audio = new AudioRecording();
        audio.setupForListen();
        audio.listen();
        window.setTimeout(() => {
            animationElement.remove();
            wrapDiv.remove();
            $(page).find('.ui-audioCurrent').removeClass('ui-audioCurrent');
        }, (duration + 1) * 1000);
    }


    // N.B. Apparently when the window is shutting down, it is still possible to return from this
    // function with window[kSocketName] undefined.
    // private getWebSocket(): WebSocket {
    //     if (!window.top[kSocketName]) {
    //         //currently we use a different port for this websocket, and it's the main port + 1
    //         const websocketPort = parseInt(window.location.port, 10) + 1;
    //         //NB: testing shows that our webSocketServer does receive a close notification when this window goes away
    //         window.top[kSocketName] = new WebSocket("ws://127.0.0.1:" + websocketPort.toString());
    //     }
    //     return window.top[kSocketName];
    // }

    public getPageFrame(): HTMLIFrameElement {
        return <HTMLIFrameElement>parent.window.document.getElementById('page');
    }

    // The body of the editable page, a root for searching for document content.
    public getPage(): JQuery {
        var page = this.getPageFrame();
        if (!page) return null;
        return $(page.contentWindow.document.body);
    }



    private fireCSharpEvent(eventName, eventData): void {
        // Note: other implementations of fireCSharpEvent have 'view':'window', but the TS compiler does
        // not like this. It seems to work fine without it, and I don't know why we had it, so I am just
        // leaving it out.
        var event = new MessageEvent(eventName, { 'bubbles': true, 'cancelable': true, 'data': eventData });
        top.document.dispatchEvent(event);
    }


}

ToolBox.getTabModels().push(new PanAndZoom());


