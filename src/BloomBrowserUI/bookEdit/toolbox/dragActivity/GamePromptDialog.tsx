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
import { splitIntoGraphemes } from "../../../utils/textUtils";

export const GamePromptDialog: React.FunctionComponent<IGamePromptDialogProps> = props => {
    const promptL10nId = props.prompt?.getAttribute("data-caption-l10nid");
    const caption = useL10n("", promptL10nId);
    // The translation group that React creates in the dialog, kept in sync with the one in the prompt
    // element in the page.
    const localTg = useRef<HTMLElement | null>();
    return (
        <BloomDialog
            id="promptDialog"
            open={props.open}
            onClose={() => props.setOpen(false)}
            onCancel={reason => {
                // For this dialog, effects are immediate. It seems more natural that most ways
                // of closing the dialog keep the currently visible changes. So if the dialog is
                // closed by the main Cancel button at the bottom, we undo the changes.
                // Other ways of closing the dialog (e.g., clicking outside it) leave the changes,
                // even though our code normally treats them as equivalent to Cancel.
                if (reason === "cancelClicked") {
                    cancel();
                }
                props.setOpen(false);
            }}
        >
            <DialogTitle
                title={caption}
                icon={false}
                preventCloseButton={true}
            ></DialogTitle>
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
                <DialogOkButton
                    onClick={() => props.setOpen(false)}
                    default={true}
                />
                <DialogCancelButton />
            </DialogBottomButtons>
        </BloomDialog>
    );
};

// These all get initialized in initializeTg, which is called each time the dialog is opened.
// Some are used by the observer that updates the page; most are needed only for cancel().
let draggableX = 0;
let draggableY = 0;
let targetX = 0;
let targetY = 0;
let originalDraggables: HTMLElement[] = [];
let originalClassLists: string[] = [];
let originalStyles: string[] = [];
let originalContents: string[] = [];
let createdBubbles: HTMLElement[] = [];
let originalTargetStyles: string[] = [];
let originalTargetContents: string[] = [];
let promptEditable: HTMLElement | null = null;
let originalPromptHtml = "";

// prompt is the hidden element in the page where we store the 'word'.
// tg is the copy of the prompt TG that we make as part of the dialog.
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
        getToolboxBundleExports()?.activateLongPressFor($(editable));
    }
    promptEditable = promptTg.getElementsByClassName(
        "bloom-editable bloom-visibility-code-on"
    )[0] as HTMLElement;
    if (!promptEditable) {
        throw new Error("No editable in dragActivity");
    }
    originalPromptHtml = promptEditable.innerHTML;
    const page = document.getElementsByClassName(
        "bloom-page"
    )[0] as HTMLElement;

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

    // From here on is specific to the letter drag activity.
    // capture where the top left draggable and target are (before we add or remove any).
    // Also capture various bits of initial state that cancel() might need.
    originalDraggables = Array.from(
        page.getElementsByClassName("bloom-textOverPicture draggable-text")
    ) as HTMLElement[];
    createdBubbles = [];
    originalClassLists = [];
    originalStyles = [];
    originalTargetStyles = [];
    originalContents = [];
    originalTargetContents = [];
    draggableX = draggableY = targetX = targetY = 1000000; // will get reduced to minimums
    for (let i = 0; i < originalDraggables.length; i++) {
        originalClassLists.push(
            originalDraggables[i].getAttribute("class") ?? ""
        );
        originalStyles.push(originalDraggables[i].getAttribute("style") ?? "");
        originalContents.push(originalDraggables[i].innerHTML);
        if (originalDraggables[i]?.classList.contains("bloom-unused-in-lang")) {
            // it won't have a meaningful position, so it would throw our calculations off.
            continue;
        }
        const target = getTarget(originalDraggables[i]);
        if (target) {
            targetX = Math.min(targetX, target.offsetLeft);
            targetY = Math.min(targetY, target.offsetTop);
        }
        originalTargetStyles.push(target?.getAttribute("style") ?? "");
        originalTargetContents.push(target?.innerHTML ?? "");

        draggableX = Math.min(draggableX, originalDraggables[i].offsetLeft);
        draggableY = Math.min(draggableY, originalDraggables[i].offsetTop);
    }
    // Set up an observer to keep the draggables in sync with the prompt during typing.
    const promptObserver = new MutationObserver(() => {
        if (!promptEditable) {
            throw new Error("No promptEditable in dragActivity");
        }
        promptEditable.innerHTML = editable.innerHTML; // copy back to the permanent element so it gets saved.
        const promptText = editable.textContent ?? "";
        // Split the prompt text into letter groups consisting of a base letter and any combining marks.
        const letters = splitIntoGraphemes(promptText);
        const draggables = Array.from(
            page.getElementsByClassName("bloom-textOverPicture draggable-text")
        ) as HTMLElement[];
        const separation = draggables[0].offsetWidth + 15; // enhance: may want to increase this
        // How many can we fit in one row inside the parent?
        const maxBubbles = Math.floor(
            (draggables[0].parentElement?.offsetWidth ?? 0 - draggableX) /
                separation
        );
        // truncate to the number of draggables we can display
        // This is important because (e.g., with autorepeat or paste) we can get a massive number of draggables
        // very quickly, and performance degrades badly, making it hard to recover. Also, until the page relaods,
        // ones beyond this would be off-page and difficult to deal with. And when it does reload they will all
        // be on top of each other and only just visible.
        letters.splice(maxBubbles);
        const newBubbles: HTMLElement[] = [];
        if (draggables.length < letters.length) {
            // We have more letters than draggables. We'll add more draggables.
            const lastDraggable = draggables[draggables.length - 1];
            for (let i = draggables.length; i < letters.length; i++) {
                const newDraggable = lastDraggable.cloneNode(
                    true
                ) as HTMLElement;
                setGeneratedBubbleId(newDraggable);
                lastDraggable.parentElement?.appendChild(newDraggable);
                makeTargetForBubble(newDraggable);
                // It's available to push letter groups into
                draggables.push(newDraggable);
                // It needs refreshBubbleEditing to be called on it later.
                newBubbles.push(newDraggable);
                // It should be removed if we Cancel. This list has a longer lifetime.
                createdBubbles.push(newDraggable);
            }
        }
        // We deliberately don't remove draggables we don't need for this word. They might be in use
        // in some other language. This loop, as well as making the ones we want have the right content,
        // makes the ones we don't want invisible and empty.
        for (let i = 0; i < draggables.length; i++) {
            const ed = draggables[i].getElementsByClassName(
                "bloom-editable bloom-visibility-code-on"
            )[0] as HTMLElement;
            const p = ed.getElementsByTagName("p")[0];
            // Ones after the number of letters we have should be empty. This helps with
            // automatically deciding which ones should be visible based on language.
            p.textContent = letters[i] ?? "";
            // up to the number of letters we have, they should be visible; others, not.
            draggables[i].classList.toggle(
                "bloom-unused-in-lang",
                i >= letters.length
            );
            getTarget(draggables[i])?.classList.toggle(
                "bloom-unused-in-lang",
                i >= letters.length
            );
            copyContentToTarget(draggables[i]);
        }
        const shuffledDraggables = draggables.slice();
        shuffledDraggables.splice(letters.length); // don't want any invisible ones taking up space
        shuffle(shuffledDraggables);
        for (let i = 0; i < shuffledDraggables.length; i++) {
            shuffledDraggables[i].style.left = `${draggableX +
                i * separation}px`;
            shuffledDraggables[i].style.top = `${draggableY}px`;
            // Note that we use draggables, not shuffledDraggables, here. We want the targets
            // in the correct order, not the random order.
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
                true, // attach events
                false // don't make it active.
            );
        });
    });
    promptObserver.observe(editable, {
        childList: true,
        subtree: true,
        characterData: true
    });
};

function cancel() {
    promptEditable!.innerHTML = originalPromptHtml;
    for (let i = 0; i < originalDraggables.length; i++) {
        originalDraggables[i].setAttribute("class", originalClassLists[i]);
        originalDraggables[i].setAttribute("style", originalStyles[i]);
        originalDraggables[i].innerHTML = originalContents[i];
        const target = getTarget(originalDraggables[i]);
        if (target) {
            target.setAttribute("style", originalTargetStyles[i]);
            target.classList.toggle(
                "bloom-unused-in-lang",
                originalDraggables[i].classList.contains("bloom-unused-in-lang")
            );
            target.innerHTML = originalTargetContents[i];
        }
    }
    createdBubbles.forEach(b => {
        getTarget(b)?.remove();
        b.remove();
    });
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
            // We don't actually store the open state anywhere, just re-render with the new value.
            setOpen={(o: boolean) => renderGamePromptDialog(root, prompt, o)}
        />,
        root
    );
}
