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

export const GamePromptDialog: React.FunctionComponent<IGamePromptDialogProps> = props => {
    const promptL10nId = props.prompt?.getAttribute("data-caption-l10nid");
    const caption = useL10n("", promptL10nId);
    const closeText = useL10n("Close", "Common.Close");
    const localTg = useRef<HTMLElement | null>();
    return (
        <ThemeProvider theme={lightTheme}>
            <Dialog open={props.open} onClose={() => props.setOpen(false)}>
                <div
                    css={css`
                        border: 2px solid black;
                        border-radius: 5px;
                        background-color: white;
                        color: black;
                        padding: 10px;
                    `}
                >
                    <div
                        css={css`
                            font-size: 20px;
                            font-weight: bold;
                            margin-bottom: 10px;
                        `}
                    >
                        {caption}
                    </div>
                    <div
                        id="promptInput"
                        ref={ref => {
                            localTg.current = ref;
                            initializeTg(props.prompt, localTg.current);
                        }}
                    />
                    <div
                        css={css`
                            display: flex;
                            justify-content: right;
                        `}
                    >
                        <Button
                            variant="text"
                            color="primary"
                            onClick={() => props.setOpen(false)}
                        >
                            {closeText}
                        </Button>
                    </div>
                </div>
            </Dialog>
        </ThemeProvider>
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
    }, 1000);

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
        const letters = Array.from(promptText); // Todo: obey digraph etc rules, maybe allow | to split.
        const draggables = Array.from(
            page.getElementsByClassName("bloom-textOverPicture draggable-text")
        ) as HTMLElement[];
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
        const separation = draggables[0].offsetWidth + 15; // enhance: may want to increase this
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
