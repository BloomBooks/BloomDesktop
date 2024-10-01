import { css, SerializedStyles } from "@emotion/react";
import * as React from "react";
import { Button, Dialog } from "@mui/material";
import { useL10n } from "../../../react_components/l10nHooks";
import { lightTheme } from "../../../bloomMaterialUITheme";

interface IGamePromptDialogProps {
    prompt: HTMLElement;
    open: boolean;
    setOpen(open: boolean): void;
}
import { ThemeProvider } from "@mui/material/styles";
import { useRef } from "react";
import { copyContentToTarget, getTarget, shuffle } from "./dragActivityRuntime";
import { setGeneratedBubbleId } from "../overlay/overlayItem";
import { adjustTarget, makeTargetForBubble } from "./dragActivityTool";
import ReactDOM = require("react-dom");
import BloomSourceBubbles from "../../sourceBubbles/BloomSourceBubbles";
import { theOneBubbleManager } from "../../js/bubbleManager";
import { Bubble } from "comicaljs";
import { getToolboxBundleExports } from "../../editViewFrame";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle
} from "../../../react_components/BloomDialog/BloomDialog";
import {
    DialogCancelButton,
    DialogOkButton
} from "../../../react_components/BloomDialog/commonDialogComponents";

export const GamePromptDialog: React.FunctionComponent<IGamePromptDialogProps> = props => {
    const promptL10nId = props.prompt?.getAttribute("data-caption-l10nid");
    const caption = useL10n("", promptL10nId);
    const localTg = useRef<HTMLElement | null>();
    return (
        <BloomDialog
            id="promptDialog"
            open={props.open}
            // Review: should one or both of these set things in the main page back the way they were when
            // we opened the dialog? Probably what is expected of a Cancel button but maybe not when
            // just clicking outside the dialog or pressing ESC?
            onClose={() => props.setOpen(false)}
            onCancel={() => props.setOpen(false)}
        >
            <DialogTitle title={caption} icon={false}></DialogTitle>
            <DialogMiddle>
                <div
                    id="promptInput"
                    ref={ref => {
                        localTg.current = ref;
                        initializeTg(props.prompt, localTg.current);
                    }}
                />
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogOkButton onClick={() => props.setOpen(false)} />
                <DialogCancelButton />
            </DialogBottomButtons>
        </BloomDialog>
    );
};

let draggableX = 20000;
let draggableY = 20000;
let targetX = 20000;
let targetY = 20000;

const initializeTg = (prompt: HTMLElement, tg: HTMLElement | null) => {
    const promptTg = prompt.getElementsByClassName(
        "bloom-translationGroup"
    )[0] as HTMLElement;
    if (!promptTg || !tg) {
        return;
    }
    tg.innerHTML = promptTg.innerHTML;
    // copy attributes
    for (let i = 0; i < promptTg.attributes.length; i++) {
        const attr = promptTg.attributes[i];
        tg.setAttribute(attr.name, attr.value);
    }
    const editable = tg.getElementsByClassName(
        "bloom-editable bloom-visibility-code-on"
    )[0] as HTMLElement;
    if (editable) {
        getToolboxBundleExports()?.loadLongpressInstructions($(editable));
    }
    const promptEditable = promptTg.getElementsByClassName(
        "bloom-editable bloom-visibility-code-on"
    )[0] as HTMLElement;
    const page = document.getElementsByClassName(
        "bloom-page"
    )[0] as HTMLElement;

    setTimeout(() => {
        const bubbles = BloomSourceBubbles.ProduceSourceBubbles(tg);
        if (bubbles) {
            BloomSourceBubbles.MakeSourceBubblesIntoQtips(
                tg,
                bubbles,
                undefined,
                true
            );
        }
        editable.focus();
    }, 0);

    // From here on is specific to the letter drag activity.
    // capture where the top left draggable and target are (before we add or remove any)
    const originalDraggables = Array.from(
        page.getElementsByClassName("bloom-textOverPicture draggable-text")
    ) as HTMLElement[];
    for (let i = 0; i < originalDraggables.length; i++) {
        const target = getTarget(originalDraggables[i]);
        if (target) {
            targetX = Math.min(targetX, target.offsetLeft);
            targetY = Math.min(targetY, target.offsetTop);
        }
        draggableX = Math.min(draggableX, originalDraggables[i].offsetLeft);
        draggableY = Math.min(draggableY, originalDraggables[i].offsetTop);
    }
    const promptObserver = new MutationObserver(() => {
        promptEditable.innerHTML = editable.innerHTML; // copy back to the permanent element so it gets saved.
        const promptText = editable.textContent ?? "";
        // Split the prompt text into letter groups consisting of a base letter and any combining marks.
        // This is necessary because the draggables are based on the letters, but the editable is based on the graphemes.
        const letters = splitIntoGraphemes(promptText);
        const draggables = Array.from(
            page.getElementsByClassName("bloom-textOverPicture draggable-text")
        ) as HTMLElement[];
        const separation = draggables[0].offsetWidth + 15; // enhance: may want to increase this
        const maxBubbles = Math.floor(
            (draggables[0].parentElement?.offsetWidth ?? 0 - draggableX) /
                separation
        );
        // truncate to the number of draggables we can display
        // This is important because (e.g., with autorepeat or paste) we can get a massive number of draggables
        // very quickly, and performance degrades badly, making it hard to recover. Also, until the page relaods,
        // ones beyond this would be off-page and difficult to deal with.
        letters.splice(maxBubbles);
        const newBubbles: HTMLElement[] = [];
        if (draggables.length > letters.length) {
            // We have more draggables than letters. We'll remove the extra ones.
            draggables
                .splice(Math.max(letters.length, 1)) // nb removes and returns them
                .forEach((elt: HTMLElement) => {
                    getTarget(elt)?.remove();
                    elt.remove();
                });
        } else if (draggables.length < letters.length) {
            // We have more letters than draggables. We'll add more draggables.
            const lastDraggable = draggables[draggables.length - 1];
            for (let i = draggables.length; i < letters.length; i++) {
                const newDraggable = lastDraggable.cloneNode(
                    true
                ) as HTMLElement;
                setGeneratedBubbleId(newDraggable);
                lastDraggable.parentElement?.appendChild(newDraggable);
                makeTargetForBubble(newDraggable);
                draggables.push(newDraggable);
                newBubbles.push(newDraggable);
            }
        }
        for (let i = 0; i < letters.length; i++) {
            const ed = draggables[i].getElementsByClassName(
                "bloom-editable bloom-visibility-code-on"
            )[0] as HTMLElement;
            const p = ed.getElementsByTagName("p")[0];
            p.textContent = letters[i];
            copyContentToTarget(draggables[i]);
        }
        let shuffledDraggables = draggables.slice();
        shuffle(shuffledDraggables);
        for (let i = 0; i < draggables.length; i++) {
            shuffledDraggables[i].style.left = `${draggableX +
                i * separation}px`;
            shuffledDraggables[i].style.top = `${draggableY}px`;
            const t = getTarget(draggables[i]);
            if (t) {
                t.style.left = `${targetX + i * separation}px`;
                t.style.top = `${targetY}px`;
            }
        }
        adjustTarget(draggables[0], getTarget(draggables[0]), true);
        // Must do this AFTER we finish setting the content. Among many other things it does,
        // it will attach a ckeditor to the new editable. That does very complex things
        // involving timeouts, and by the end of the process, the text gets set back to
        // what it was when we started adding the ckeditor. So changes we make after that
        // get lost.
        newBubbles.forEach((newDraggable: HTMLElement) => {
            theOneBubbleManager!.refreshBubbleEditing(
                newDraggable.closest(".bloom-imageContainer") as HTMLElement,
                new Bubble(newDraggable),
                true,
                false
            );
        });
    });
    promptObserver.observe(editable, {
        childList: true,
        subtree: true,
        characterData: true
    });
};

export function splitIntoGraphemes(text: string): string[] {
    // Regular expression to match a base character (or space) followed by any number of diacritics
    const graphemeRegex = /(\p{L}| )\p{M}*/gu;
    return text.match(graphemeRegex) || [];
}

export function renderGamePromptDialog(
    root: HTMLElement,
    prompt: HTMLElement,
    open: boolean
) {
    ReactDOM.render(
        <GamePromptDialog
            prompt={prompt}
            open={open}
            setOpen={(o: boolean) => renderGamePromptDialog(root, prompt, o)}
        />,
        root
    );
}
