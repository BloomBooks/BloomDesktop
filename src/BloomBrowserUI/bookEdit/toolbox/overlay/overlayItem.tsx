/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { OverlayTool } from "./overlayTool";
import { Div, Span } from "../../../react_components/l10nComponents";
import { kBloomBlue, kBloomGray } from "../../../utils/colorUtils";
import {
    adjustTarget,
    enableDraggingTargets,
    makeTargetForBubble
} from "../dragActivity/dragActivityTool";
import {
    ImagePlaceholderIcon,
    WrongImagePlaceholderIcon
} from "../../../react_components/icons/ImagePlaceholderIcon";
import theOneLocalizationManager from "../../../lib/localizationManager/localizationManager";
import { SignLanguageIcon } from "../../../react_components/icons/SignLanguageIcon";
import { GifIcon } from "../../../react_components/icons/GifIcon";
import { theOneBubbleManager } from "../../js/bubbleManager";
import { Bubble, Comical } from "comicaljs";
import { Point } from "../../js/point";

const ondragstart = (
    ev: React.DragEvent<HTMLElement> | React.DragEvent<SVGSVGElement>,
    style: string
) => {
    // Here "text/x-bloombubble" is a unique, private data type recognised
    // by ondragover and ondragdrop methods that BubbleManager
    // attaches to bloom image containers. It doesn't make sense to
    // drag these objects anywhere else, so they don't need any of
    // the common data types. Using a private type means that other drop handlers
    // will not accept them. It is often recommended to include a text/plain value,
    // but it really doesn't make sense to drop the text associated with these
    // bubbles anywhere outside Bloom. I believe the text/x- prefix makes these
    // valid (unregistered) mime types, which technically this argument is supposed
    // to be.
    ev.dataTransfer.setData("text/x-bloombubble", style);
    ev.dataTransfer.setData("text/x-bloomdraggable", "true");
    const target = ev.currentTarget as HTMLElement;
    const rect = target.getBoundingClientRect();
    // add this (typically negative) amount to drop.clientY to get where the top of the new bubble should be
    const top = (rect.top - ev.clientY) / Point.getScalingFactor();
    // add this (typically positive) amount to drop.clientX to get where the right of the new bubble should be
    const right = (rect.right - ev.clientX) / Point.getScalingFactor();
    // This bizarre trick is necessary because the drag-and-drop security model won't let ondragend
    // access the content of a data transfer item, but it can see all their names.
    ev.dataTransfer.setData(`text/x-right-top-offset:${right},${top}`, "");
};

// When a template bubble is dropped on the image, we create a new bubble
const ondragend = (
    ev: React.DragEvent<HTMLElement> | React.DragEvent<SVGSVGElement>,
    style: string,
    makeTarget: boolean,
    makeMatchingTextBox: boolean,
    addClasses?: string,
    contentL10nKey?: string, // used to pre-populate relevant alternatives
    hintL10nKey?: string,
    // Anything extra the client wants to do to the bubble
    extraAction?: (top: HTMLElement) => void,
    userDefinedStyleName?: string // bubble should get this plus "-style" (or Bubble-style) in class list
) => {
    const bubbleManager = OverlayTool.bubbleManager();
    if (!bubbleManager) {
        // This check is mainly to keep lint happy. We should never get here.
        console.error("No bubble manager at end of drag.");
        return;
    }
    let rightTopOffset = "";
    for (let i = 0; i < ev.dataTransfer.items.length; i++) {
        if (
            ev.dataTransfer.items[i].type.startsWith("text/x-right-top-offset:")
        ) {
            rightTopOffset = ev.dataTransfer.items[i].type.substring(
                "text/x-right-top-offset:".length
            );
            break;
        }
    }

    const bubble = bubbleManager.addOverPictureElementWithScreenCoords(
        ev.screenX,
        ev.screenY,
        style,
        userDefinedStyleName,
        rightTopOffset
    );
    if (!bubble) return;
    if (extraAction) {
        extraAction(bubble);
    }
    if (addClasses) {
        // trim because an exception is thrown if we try to add a class that is empty,
        // which we will otherwise do if there is a leading or trailing space.
        bubble.classList.add(...addClasses.trim().split(" "));
    }
    //Slider: if (makeMatchingTextBox) {
    //     // Currently only true for drag-word-chooser-slider. The new element is a picture box.
    //     // We want a corresponding text box, a clone of an existing bloom-wordChoice.
    //     // The two should be linked by a unique value for data-img-txt on the image
    //     // and data-txt-img on the text.
    //     const existing = Array.from(
    //         bubble.ownerDocument.getElementsByClassName("bloom-wordChoice")
    //     );
    //     const pattern = existing[0];
    //     if (pattern) {
    //         // should always be one, it's on the template. Unless the user deletes them all!
    //         // That will just mess things up for now. Eventually, we might make code to
    //         // re-create it, but where?
    //         // Note that this duplication causes it to be in the same place as the pattern.
    //         // It will be invisible until we select the corresponding bubble.
    //         const newTextBox = pattern.cloneNode(true) as HTMLElement;
    //         // Enhance: do something about the value pathologically not parsing
    //         const existingIds = existing.map(el =>
    //             parseInt(el.getAttribute("data-txt-img") ?? "1")
    //         );
    //         const newId = "" + (Math.max(...existingIds) + 1);
    //         bubble.setAttribute("data-img-txt", newId);
    //         newTextBox.setAttribute("data-txt-img", newId);
    //         // The order doesn't matter much, but keeping them in the order created feels right,
    //         // and makes sure we will find the original when making the next clone.
    //         bubble.parentElement?.insertBefore(
    //             newTextBox,
    //             existing[existing.length - 1].nextElementSibling
    //         );
    //         // remove any copied content (do we have a common function to do this somewhere?)
    //         Array.from(
    //             newTextBox.getElementsByClassName("bloom-editable")
    //         ).forEach(editable => (editable.innerHTML = "<p></p>"));
    //     }
    // }
    let langsToWaitFor = 0;
    if (contentL10nKey) {
        const settings = bubbleManager.getSettings();
        const langs = [settings.languageForNewTextBoxes];
        if (
            settings.currentCollectionLanguage2 &&
            !langs.includes(settings.currentCollectionLanguage2)
        ) {
            langs.push(settings.currentCollectionLanguage2);
        }
        if (
            settings.currentCollectionLanguage3 &&
            !langs.includes(settings.currentCollectionLanguage3)
        ) {
            langs.push(settings.currentCollectionLanguage3);
        }
        if (!langs.includes("en")) {
            langs.push("en");
        }
        langsToWaitFor = langs.length;
        langs.forEach(lang => {
            theOneLocalizationManager
                .asyncGetTextInLang(contentL10nKey!, "", lang, "")
                .then(text => {
                    const editables = Array.from(
                        bubble.getElementsByClassName("bloom-editable")
                    );
                    const prototype = editables[0];
                    let editableInLang = editables.find(
                        e => e.getAttribute("lang") === lang
                    );
                    if (!editableInLang) {
                        editableInLang = prototype.cloneNode(
                            true
                        ) as HTMLElement;
                        editableInLang.setAttribute("lang", lang);
                        // only the primary language should be visible  in the bubble, and it should
                        // be present already, so we won't be adding it. But it will be the prototype,
                        // so we need to get rid of these classes. We'll get a more exactly correct
                        // set of visibility classes when the page next loads, but we'd prefer not
                        // to go through that rather time-consuming process right now.
                        editableInLang.classList.remove(
                            "bloom-visibility-code-on",
                            "bloom-content1"
                        );
                        prototype.parentElement!.appendChild(editableInLang);
                    }
                    editableInLang.getElementsByTagName(
                        "p"
                    )[0].textContent = text;
                    langsToWaitFor--;
                });
        });
    }
    if (hintL10nKey) {
        const tg = bubble.getElementsByClassName("bloom-translationGroup")[0];
        tg.setAttribute("data-hint", hintL10nKey);
    }
    if (contentL10nKey || hintL10nKey) {
        // This is to allow for the possibility of the toolbox containing a template
        // that has some known content for which we might want to provide a hint,
        // or which might already have content in several languages. I don't think
        // there are any current examples, but early in the development of Bloom games,
        // There were several text buttons (Check, Try Again, Show Answer) for which
        // this was useful, and it could happen again.
        const addBubbles = () => {
            if (langsToWaitFor) {
                setTimeout(addBubbles, 100);
                return;
            }
            const tg = bubble.getElementsByClassName(
                "bloom-translationGroup"
            )[0] as HTMLElement;
            bubbleManager.addSourceAndHintBubbles(tg);
        };
        addBubbles(); // Do now if we can, if not, sometime when we've gotten all the localizations.
    }
    if (makeTarget) {
        setGeneratedBubbleId(bubble);
        bubble.style.width = ev.currentTarget.clientWidth + "px";
        makeTargetForBubble(bubble);
    }
    // This must be done AFTER we give the bubble its id if we're going to, because that's how we know
    // it's one of the ones that should be ordered to the end.
    bubbleManager.adjustBubbleOrdering();
};

// Make a unique id for the bubble, and set it on the bubble.
// It's good enough to be unique within the current page, and will very probably
// be unique throughout the document, without being quite as long and ugly as a guid.
export const setGeneratedBubbleId = (bubble: HTMLElement): string => {
    let id = Math.random()
        .toString(36)
        .substring(2, 9);
    while (bubble.ownerDocument.querySelector(`[data-bubble-id="${id}"]`)) {
        id = Math.random()
            .toString(36)
            .substring(2, 9);
    }
    bubble.setAttribute("data-bubble-id", id);
    return id;
};

// A wrapper for something that is an overlay source icon, typically an SVG.
// Supports dragging it onto the canvas.
export const OverlaySvgItem: React.FunctionComponent<{
    style: string;
    makeTarget?: boolean;
    makeMatchingTextBox?: boolean;
    addClasses?: string;
    extraAction?: (top: HTMLElement) => void;
}> = props => {
    return (
        <div // infuriatingly, svgs don't support draggable, so we have to wrap.
            css={css`
                width: 50px;
                height: 50px;
                cursor: grab;
            `}
            draggable={true}
            onDragStart={ev => ondragstart(ev, props.style)}
            onDragEnd={ev =>
                ondragend(
                    ev,
                    props.style,
                    props.makeTarget ?? false,
                    props.makeMatchingTextBox ?? false,
                    props.addClasses,
                    undefined,
                    undefined,
                    props.extraAction
                )
            }
        >
            {props.children}
        </div>
    );
};

export const OverlayImageItem: React.FunctionComponent<{
    style: string;
    makeTarget?: boolean;
    makeMatchingTextBox?: boolean;
    addClasses?: string;
    color?: string;
    strokeColor?: string;
}> = props => {
    return (
        <OverlaySvgItem
            style={props.style}
            makeTarget={props.makeTarget}
            makeMatchingTextBox={props.makeMatchingTextBox}
            addClasses={props.addClasses}
        >
            <ImagePlaceholderIcon
                css={css`
                    width: 50px;
                    height: 50px;
                    cursor: grab;
                `}
                color={props.color}
                strokeColor={props.strokeColor}
            />
        </OverlaySvgItem>
    );
};

export const OverlayWrongImageItem: React.FunctionComponent<{
    style: string;
    makeTarget?: boolean;
    makeMatchingTextBox?: boolean;
    addClasses?: string;
    color?: string;
    strokeColor?: string;
    extraAction?: (top: HTMLElement) => void;
}> = props => {
    return (
        <OverlaySvgItem
            style={props.style}
            makeTarget={props.makeTarget}
            makeMatchingTextBox={props.makeMatchingTextBox}
            addClasses={props.addClasses}
            extraAction={props.extraAction}
        >
            <WrongImagePlaceholderIcon
                css={css`
                    width: 50px;
                    height: 50px;
                    cursor: grab;
                `}
                color={props.color}
                strokeColor={props.strokeColor}
            />
        </OverlaySvgItem>
    );
};

export const OverlayGifItem: React.FunctionComponent<{
    style: string;
    addClasses?: string;
    color?: string;
    strokeColor?: string;
}> = props => {
    return (
        <OverlaySvgItem
            style={props.style}
            makeTarget={false}
            addClasses={"bloom-gif " + (props.addClasses ?? "")}
        >
            <GifIcon
                css={css`
                    width: 50px;
                    height: 50px;
                    cursor: grab;
                `}
                color={props.color}
                strokeColor={props.strokeColor}
            />
        </OverlaySvgItem>
    );
};

export const OverlayVideoItem: React.FunctionComponent<{
    style: string;
    makeTarget?: boolean;
    // We could easily add makeTarget?: boolean; but we don't want to allow video dragging in the finished book
    addClasses?: string;
    color?: string;
}> = props => {
    return (
        <OverlaySvgItem
            style={props.style}
            addClasses={props.addClasses}
            makeTarget={props.makeTarget}
        >
            <SignLanguageIcon
                css={css`
                    width: 50px;
                    height: 50px;
                    cursor: grab;
                `}
                color={props.color ?? "white"}
                //strokeColor={props.strokeColor}
            />
        </OverlaySvgItem>
    );
};

export const OverlayItem: React.FunctionComponent<{
    src: string;
    style: string;
    makeTarget?: boolean;
    addClasses?: string;
    userDefinedStyleName?: string;
}> = props => {
    return (
        <img
            css={css`
                width: 50px;
                height: 50px;
                cursor: grab;
            `}
            src={props.src}
            draggable={true}
            onDragStart={ev => ondragstart(ev, props.style)}
            onDragEnd={ev =>
                ondragend(
                    ev,
                    props.style,
                    props.makeTarget ?? false,
                    false, // don't make a matching text box
                    props.addClasses,
                    undefined,
                    undefined,
                    undefined,
                    props.userDefinedStyleName
                )
            }
        />
    );
};

export const OverlayTextItem: React.FunctionComponent<{
    l10nKey: string;
    style: string;
    className?: string;
    makeTarget?: boolean;
    addClasses?: string;
    contentL10nKey?: string;
    hintL10nKey?: string;
    hide?: boolean; // If true, we don't want this item at all.
    userDefinedStyleName?: string;
}> = props => {
    if (props.hide) {
        return null;
    }
    return (
        <Span
            l10nKey={props.l10nKey}
            className={props.className}
            draggable={true}
            onDragStart={ev => ondragstart(ev, props.style)}
            onDragEnd={ev =>
                ondragend(
                    ev,
                    props.style,
                    props.makeTarget ?? false,
                    false, // don't make a matching text box
                    props.addClasses,
                    props.contentL10nKey,
                    props.hintL10nKey,
                    undefined,
                    props.userDefinedStyleName
                )
            }
        ></Span>
    );
};

const buttonItemProps = css`
    margin-left: 5px;
    text-align: center;
    padding: 2px 0.5em;
    vertical-align: middle;
    color: ${kBloomBlue};
    background-color: "white";
    box-shadow: 0px 4px 4px rgba(0, 0, 0, 0.2);
`;

export const OverlayButtonItem: React.FunctionComponent<{
    l10nKey: string;
    addClasses: string;
    contentL10nKey?: string;
    hintL10nKey?: string;
    userDefinedStyleName?: string;
}> = props => {
    return (
        <OverlayTextItem
            css={buttonItemProps}
            l10nKey={props.l10nKey}
            addClasses={props.addClasses}
            makeTarget={false}
            style="none"
            contentL10nKey={props.contentL10nKey}
            hintL10nKey={props.hintL10nKey}
            userDefinedStyleName={props.userDefinedStyleName}
        ></OverlayTextItem>
    );
};

export const OverlayItemRow: React.FunctionComponent<{
    children: React.ReactNode;
    secondRow?: boolean;
}> = props => {
    return (
        <div
            css={css`
                // Using display: flex helps us grow some of the children
                // while also allowing us to adapt to the presence or absence of the vertical scrollbar
                display: flex;

                // Each row fills the entire width of the parent horizontally
                width: 100%;
                height: 50px;
                align-items: center; // vertical
                justify-content: space-around; // horizontal
                margin-right: 4px; // matches the space on the left

                // Each row gets a little vertical cushion
                margin-top: 10px;
                ${props.secondRow ? "margin-top: 0; margin-bottom: 10px;" : ""}
            `}
        >
            {props.children}
        </div>
    );
};

export const OverlayItemRegion: React.FunctionComponent<{
    children: React.ReactNode;
    className?: string;
    l10nKey?: string;
    theme?: string;
}> = props => {
    const bgColor = props.theme === "blueOnTan" ? "white" : kBloomGray;
    const fgColor = props.theme === "blueOnTan" ? kBloomBlue : "white";
    return (
        <div
            css={css`
                background-color: ${bgColor};
                padding: 6px;
                display: flex;
                flex-wrap: wrap;
            `}
            className={props.className}
        >
            {props.l10nKey === "" || (
                <Div
                    css={css`
                        color: ${fgColor};
                        font-weight: bold;
                    `}
                    l10nKey={
                        props.l10nKey ??
                        "EditTab.Toolbox.ComicTool.DragInstructions"
                    }
                    className="overlayToolControlDragInstructions"
                >
                    Drag any of these overlays onto the image:
                </Div>
            )}
            {props.children}
        </div>
    );
};
