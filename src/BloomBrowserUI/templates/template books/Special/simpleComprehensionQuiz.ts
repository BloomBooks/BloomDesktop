// The js generated from this file is used in the template page generated from simpleComprehensionQuiz.pug.
// It makes sure the body element has the editMode class (if the editMode stylesheet is loaded)
// and installs appropriate click handlers (depending on edit mode) which manipulate the classes
// of .checkbox-and-textbox-answer elements to produce the desired checking and dimming of right
// and wrong answers. It also adds an appropriate class to answers that are empty (to hide them or
// dim them) and plays appropriate sounds when a right or wrong answer is chosen.
// Eventually it will cooperate with reader code to handle analytics.
// The output is also part of the bloom-player, which creates instances of the new simpleComprehensionQuiz
// pages dynamically in order to handle old-style comprehension questions represented as json,
// and needs the associated JS to make them work.

// Master function, called when document is ready, initialized CQ pages
function init(): void {
    ensureEditModeStyleSheet();
    initAnswerWidgets();
}

//------------ Code involved in setting the editMode class on the body element when appropriate-----
function ensureEditModeStyleSheet() {
    if (!inEditMode()) {
        return;
    }
    // with future Bloom versions, this might already be there,
    // but it doesn't matter if it is.
    document.body.classList.add("editMode");
}

function inEditMode() {
    for (var i = 0; i < document.styleSheets.length; i++) {
        const href = document.styleSheets[i].href;
        if (href && href.endsWith("editMode.css")) {
            return true;
        }
    }
    return false;
}

//------------ Code for managing the answer widgets-------

// Initialize the answer widgets, arranging for the appropriate click actions
// and for maintaining the class that indicates empty answers.
// Assumes the code that sets up the editMode class on the body element if appropriate has already been run.
function initAnswerWidgets(): void {
    markEmptyAnswers();
    const observer = new MutationObserver(markEmptyAnswers);
    observer.observe(document.body, { characterData: true, subtree: true });
    const list = document.getElementsByClassName("styled-check-box");
    for (let i = 0; i < list.length; i++) {
        let x = list[i];
        if (document.body.classList.contains("editMode")) {
            x.addEventListener("click", handleEditModeClick);
        } else {
            x!.parentElement!.addEventListener("click", handleReadModeClick);
        }
    }
}

function handleEditModeClick(evt: Event): void {
    const target = evt.target as HTMLElement;
    if (target && target.parentElement) {
        target.parentElement.classList.toggle("correct-answer");
    }
}

function handleReadModeClick(evt: Event): void {
    const currentTarget = evt.currentTarget as HTMLElement;
    const classes = currentTarget.classList;
    classes.add("user-selected");
    const correct = classes.contains("correct-answer");
    const soundUrl = correct ? "right_answer.mp3" : "wrong_answer.mp3";
    playSound(soundUrl);
    // Make the state of the hidden input conform (for screen readers). Only if the
    // correct answer was clicked does the checkbox get checked.
    const checkBox = currentTarget.getElementsByClassName(
        "hiddenCheckbox"
    )[0] as HTMLInputElement;
    if (checkBox) {
        checkBox.checked = correct;
    }

    // This might cause us to send analytics information...tell the app if it's interested.
    if ((window as any).analyticsChange) {
        (window as any).analyticsChange();
    }
}

function playSound(url) {
    const player = getPagePlayer();
    player.setAttribute("src", url);
    player.play();
}

function getPagePlayer(): HTMLAudioElement {
    let player: HTMLAudioElement | null = document.querySelector(
        "#quiz-sound-player"
    ) as HTMLAudioElement;
    if (player && !player.play) {
        player.remove();
        player = null;
    }
    if (!player) {
        player = document.createElement("audio");
        player.setAttribute("id", "#quiz-sound-player");
        document.body.appendChild(player);
    }
    return player;
}

function markEmptyAnswers() {
    const answers = document.getElementsByClassName(
        "checkbox-and-textbox-answer"
    );
    for (let i = 0; i < answers.length; i++) {
        if (hasContent(answers[i])) {
            answers[i].classList.remove("empty");
        } else {
            answers[i].classList.add("empty");
        }
    }
}

function hasContent(answer) {
    const editables = answer.getElementsByClassName("bloom-editable");
    for (let j = 0; j < editables.length; j++) {
        const editable = editables[j];
        if (
            editable.classList.contains("bloom-visibility-code-on") &&
            editable.textContent.trim()
        ) {
            return true;
        }
    }
    return false;
}

// In some cases (loading into a bloom reader carousel, for example) the page may already be loaded.
if (document.readyState === "complete") {
    init();
} else {
    window.addEventListener("load", () => {
        init();
    });
}
