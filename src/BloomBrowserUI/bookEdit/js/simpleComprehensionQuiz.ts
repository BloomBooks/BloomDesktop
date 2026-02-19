// Note to future selves: In looking at BL-10037, Andrew and Gordon looked for some time to see how
// this file might be loaded by bloom-player. It isn't. The somewhat slimmed down file
// ".\src\activities\domActivities\SimpleCheckboxQuiz.ts" is included instead. In fact the solution
// for BL-10037 turned out to be fixing an oversimplification of the original player code in that version.
// Since that file is used in the player, this file has been modified to handle only editing. (BL-14565)

// The js generated from this file is used in editing template pages generated from simpleComprehensionQuiz.pug.
// It makes sure the body element has the editMode class (if the editMode stylesheet is loaded)
// and installs click handlers which manipulate the classes of .checkbox-and-textbox-choice elements
// to produce the desired checking of right and wrong answers.

//------------ Code for editing the choice widgets -------

function inEditMode(): boolean {
    for (let i = 0; i < document.styleSheets.length; i++) {
        const href = document.styleSheets[i].href;
        if (href && href.endsWith("editMode.css")) {
            return true;
        }
    }
    return false;
}

// Initialize the choice widgets, arranging for the appropriate click actions
// and for maintaining the class that indicates empty choice.
export function initChoiceWidgetsForEditing(): void {
    // Double-check that we are in edit mode before setting up for editing.
    if (!inEditMode()) {
        return;
    }
    // Double-check that we are in a single page that is a simple comprehension quiz.
    const pages = document.getElementsByClassName("bloom-page");
    const quizPages = document.getElementsByClassName(
        "simple-comprehension-quiz",
    );
    if (pages.length !== 1 || quizPages.length !== 1) {
        return;
    }

    // This is needed for CSS to work properly while editing.
    document.body.classList.add("editMode");

    markEmptyChoices();
    const observer = new MutationObserver(markEmptyChoices);
    observer.observe(document.body, { characterData: true, subtree: true });
    const list = document.getElementsByClassName("checkbox-and-textbox-choice");
    for (let i = 0; i < list.length; i++) {
        const x = list[i] as HTMLElement;
        const checkbox = getCheckBox(x);
        const correct = x.classList.contains("correct-answer");
        checkbox.addEventListener("click", handleEditModeClick);
        // Not sure why this doesn't get persisted along with the correct-answer class,
        // but glad it doesn't, because we don't want it to show up even as a flash
        // in reader mode.
        checkbox.checked = correct;
    }
}

function getCheckBox(holder: HTMLElement): HTMLInputElement {
    return holder.firstElementChild as HTMLInputElement;
}

function handleEditModeClick(evt: Event): void {
    const target = evt.target as HTMLInputElement;
    if (!target) {
        return;
    }
    const wrapper = (evt.currentTarget as HTMLElement).parentElement;
    if (target.checked) {
        wrapper!.classList.add("correct-answer");
    } else {
        wrapper!.classList.remove("correct-answer");
    }
}

function markEmptyChoices(): void {
    const choices = document.getElementsByClassName(
        "checkbox-and-textbox-choice",
    );
    for (let i = 0; i < choices.length; i++) {
        if (hasVisibleContent(choices[i])) {
            choices[i].classList.remove("empty");
        } else {
            choices[i].classList.add("empty");
        }
    }
}

function hasVisibleContent(choice: Element): boolean {
    const editables = choice.getElementsByClassName("bloom-editable");

    return Array.from(editables).some(
        (e) =>
            e.classList.contains("bloom-visibility-code-on") &&
            (e.textContent || "").trim() !== "",
    );
}
