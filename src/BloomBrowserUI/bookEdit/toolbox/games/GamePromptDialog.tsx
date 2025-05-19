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
import {
    adjustDraggablesForLanguage,
    copyContentToTarget,
    getTarget,
    shuffle,
    isTheTextInDraggablesTheSame
} from "bloom-player";
import { setGeneratedDraggableId } from "../overlay/CanvasElementItem";
import {
    adjustTarget,
    GameTool,
    makeTargetForDraggable
} from "../games/GameTool";
import * as ReactDOM from "react-dom";
import BloomSourceBubbles from "../../sourceBubbles/BloomSourceBubbles";
import {
    CanvasElementManager,
    theOneCanvasElementManager
} from "../../js/CanvasElementManager";
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
import { kCanvasElementClass } from "../overlay/canvasElementUtils";
import { kBloomCanvasSelector } from "../../js/bloomImages";

export const GamePromptDialog: React.FunctionComponent<IGamePromptDialogProps> = props => {
    const promptL10nId = props.prompt?.getAttribute("data-caption-l10nid");
    const caption = useL10n("", promptL10nId);
    // The translation group that React creates in the dialog, kept in sync with the one in the prompt
    // element in the page.
    const localTg = useRef<HTMLElement | null>();
    const [haveLocalTg, setHaveLocalTg] = React.useState(false);
    const closeDialog = () => {
        BloomSourceBubbles.removeSourceBubbles(localTg.current!);
        props.setOpen(false);
    };
    React.useEffect(() => {
        if (props.open && localTg.current) {
            // The dialog is being opened. Initialize everything.
            // We really only want to do this once, especially when first switching to a new
            // language, because we want to capture the positions of the visible boxes before
            // adjustDraggablesForLanguage hides most of them, and also because we capture state
            // here that is used to restore things on Cancel.
            initializeDialog(props.prompt, localTg.current);
        }
        if (!localTg.current) {
            // So we'll get rendered when we get the ref.
            setHaveLocalTg(false);
        }
    }, [props.open, props.prompt, haveLocalTg]);
    return (
        <BloomDialog
            id="promptDialog"
            open={props.open}
            onKeyDownCapture={e => {
                if (e.key === "Enter") {
                    e.preventDefault();
                    closeDialog();
                }
            }}
            onClose={closeDialog}
            onCancel={reason => {
                // For this dialog, effects are immediate. It seems more natural that most ways
                // of closing the dialog keep the currently visible changes. So if the dialog is
                // closed by the main Cancel button at the bottom, we undo the changes.
                // Other ways of closing the dialog (e.g., clicking outside it) leave the changes,
                // even though our code normally treats them as equivalent to Cancel.
                if (reason === "cancelClicked") {
                    cancel();
                }
                closeDialog();
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
                        if (ref && !haveLocalTg) {
                            setHaveLocalTg(true);
                        }
                    }}
                />
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogOkButton onClick={closeDialog} default={true} />
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
// This method initializes it and captures a lot of other stuff we want to
// save from the initial state of the page, including the positions where
// we should put letters and the state we should restore if cancelled.
// It's important (but I don't know how to ensure it) that when switching to
// a new language, this method is called before adjustDraggablesForLanguage
// is called by anything else, so that the positions we use as a starting point
// for the new language are based on the letters that were previously visible.
// At present, calling the dialog happens quite early in bootstrapping the page,
// while the only other call to adjustDraggablesForLanguage is in from code
// in the dragActivityTool's newPageReady, which is called later.
const initializeDialog = (prompt: HTMLElement, tg: HTMLElement | null) => {
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
    // This interception prevents pasting anything but plain text (e.g., we don't want
    // HTML markup, possibly forcing an unwanted font into our document).
    editable.addEventListener("paste", event => {
        event.preventDefault();
        const text = event.clipboardData?.getData("text");
        if (text) {
            // There are undeprecated ways of inserting the text, but this also puts the
            // change into the browser undo stack as expected, and nothing else I can find
            // does so (short of attaching CkEditor). Hopefully, if execCommand ever really
            // goes away, by then there will be a way to insert a change into the Undo stack.
            document.execCommand("insertText", false, text);
        }
    });
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

    let startTryingToFocus = Date.now();
    // The prompt looks strange and the user can't type until we actually get focus onto the
    // editable. For some reason, simply calling focus doesn't always work.
    const tryToFocus = () => {
        if (
            !document.contains(editable) &&
            Date.now() - startTryingToFocus > 2000
        ) {
            // If the document doesn't contain the editable and it's been more than 2s,
            // Possibly the dialog has been closed, so we should stop trying to focus.
            return;
        }
        if (
            !editable.contains(document.activeElement) &&
            // I don't know how this can be false, but in my testing it often was and this loop
            // could become infinite since it's not possible to focus something that's not in
            // the document. Possibly something to do with the dialog being repeatedly
            // initialized, a problem I fixed in a parallel PR.
            document.contains(editable)
        ) {
            editable.focus();
            // keep trying to focus it until it is. Usually only takes a couple of tries at most.
            setTimeout(tryToFocus, 100);
        } else {
            // Finally got focus. But occasionally, especially with slower computers, we lose it
            // again almost at once. One time it switched, for no reason I know of, to the
            // Start button in game setup mode. Tab had not been pressed, and I'm pretty sure none
            // of our code tries to put focus there, so I'm guessing it was some kind of browser
            // built-in behavior (maybe it tries to focus something if the page doesn't do it
            // soon enough??). In such cases we want to make yet another attempt to focus the
            // thing we want. However, it's also possible that the user is trying to tab to the
            // OK button or something like that, so we don't try again if there's a relatedTarget
            // that's in the dialog.)
            editable?.addEventListener(
                "focusout",
                e => {
                    if (Date.now() - startTryingToFocus < 500) {
                        // If we lose focus too quickly, it's likely the browser is somehow stealing
                        // focus.
                        const dlg = tg.closest(".MuiPaper-root") as HTMLElement;
                        if (
                            e.relatedTarget &&
                            dlg &&
                            dlg.contains(e.relatedTarget as Node)
                        ) {
                            // tabbed to something in the dialog, let it stand
                            return;
                        }
                        // We can take this out evenually, but for now I want to ask our testers to look for it.
                        console.log("Restoring stolen focus!");
                        tryToFocus();
                    }
                },
                { once: true }
            );
        }
    };
    tryToFocus();

    // From here on is specific to the letter drag activity.
    // capture where the top left draggable and target are (before we add or remove any).
    // Also capture various bits of initial state that cancel() might need.
    originalDraggables = Array.from(
        page.getElementsByClassName(kCanvasElementClass + " draggable-text")
    ) as HTMLElement[];
    createdBubbles = [];
    originalClassLists = [];
    originalStyles = [];
    originalTargetStyles = [];
    originalContents = [];
    originalTargetContents = [];
    draggableX = draggableY = targetX = targetY = 1000000; // will get reduced to minimums
    const draggables = Array.from(
        page.querySelectorAll("[data-draggable-id]")
    ) as HTMLElement[];
    const first = draggables[0];
    const firstEditable = first.getElementsByClassName(
        "bloom-editable bloom-visibility-code-on"
    )[0];
    if (!promptEditable.textContent?.trim()) {
        // We are probably showing this language for the first time, and only one letter is going
        // to be visible initially. We want to move that first letter to the
        // left-most position, since it will soon be the only one visible, and we dont' want to mess
        // up the starting place for new letters.
        const minx = Math.min(
            ...draggables
                .filter(
                    (d, index) =>
                        index === 0 ||
                        !d.classList.contains("bloom-unused-in-lang")
                )
                .map(d => d.offsetLeft)
        );
        first.style.left = minx + "px";
        // remember it to make sure we never take this branch again for this language
        CanvasElementManager.saveStateOfCanvasElementAsCurrentLangAlternate(
            first
        );
    }
    // Adjust which targets are visible based on the current language.
    // Must be after the adjustment of the first draggable, since it may affect which ones are unused.
    adjustDraggablesForLanguage(prompt.closest(".bloom-page") as HTMLElement);
    // Capture the state we want to restore on Cancel and the start positions for draggable and target rows
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
        if (target && target.offsetLeft < targetX) {
            // capture a starting place for new rows of targets from the left-most target.
            // This allows just one to be dragged to control them all; otherwise, moving the
            // row down would be difficult since they would all have to be moved.
            targetX = target.offsetLeft;
            targetY = target.offsetTop;
        }
        originalTargetStyles.push(target?.getAttribute("style") ?? "");
        originalTargetContents.push(target?.innerHTML ?? "");
        if (originalDraggables[i].offsetLeft < draggableX) {
            draggableX = originalDraggables[i].offsetLeft;
            draggableY = originalDraggables[i].offsetTop;
        }
    }
    const setDraggableText = (draggable: HTMLElement, text: string) => {
        const ed = draggable.getElementsByClassName(
            "bloom-editable bloom-visibility-code-on"
        )[0] as HTMLElement;
        // Text elements that are the output of a prompt dialog should not have source bubbles.
        // We'll reserve that for the dialog.
        ed?.parentElement?.classList.add("bloom-no-source-bubble");
        const p = ed?.getElementsByTagName("p")?.[0];
        // one use of this method is to clear the text, in which case, it's fine
        // for there to be no p present to clear. But if we're trying to set
        // text, there needs to be one.
        if (!p) {
            console.assert(
                !text,
                "Expected to set a non-empty string, but found no paragraph"
            );
            return;
        }
        p.textContent = text ?? "";
        // We're not putting any placeholder attrs on the letters, so we don't need
        // to handle those.
    };
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
            page.getElementsByClassName(kCanvasElementClass + " draggable-text")
        ) as HTMLElement[];
        // make sure we get some reasonable offsetWidth for the first one, if there are
        // any letters. (Can become display:none if we have no letters.)
        setDraggableText(draggables[0], letters[0]);
        draggables[0].classList.remove("bloom-unused-in-lang");
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
                setGeneratedDraggableId(newDraggable);
                // Ensure the new draggable starts out empty.  See BL-14348.
                // (This covers all languages present, visible or not.)
                const paras = newDraggable.querySelectorAll(
                    "div.Letter-style>p"
                );
                paras.forEach(p => {
                    p.textContent = "";
                });
                lastDraggable.parentElement?.appendChild(newDraggable);
                makeTargetForDraggable(newDraggable);
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
            setDraggableText(draggables[i], letters[i]);
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
        shuffle(shuffledDraggables, isTheTextInDraggablesTheSame);
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
            theOneCanvasElementManager!.refreshCanvasElementEditing(
                newDraggable.closest(kBloomCanvasSelector) as HTMLElement,
                new Bubble(newDraggable),
                true, // attach events
                false // don't make it active.
            );
        });
        // This seems to at least somewhat reduce the likelihood of losing focus
        // after a keystroke, especially with Keyman multi-character inserts (BL-14098).
        // I think it is harmless, since I can't think of any case where the text in the
        // input changes and we wouldn't want it focused afterwards.
        editable.focus();
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
