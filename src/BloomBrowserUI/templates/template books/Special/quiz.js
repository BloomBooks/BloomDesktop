function init() {
    ensureEditModeStyleSheet();
    markEmptyAnswers();
    var observer = new MutationObserver(markEmptyAnswers);
    observer.observe(document.body, { characterData: true, subtree: true });
    var list = document.getElementsByClassName("quizCheck");
    for (var i = 0; i < list.length; i++) {
        var x = list[i];
        if (document.body.classList.contains("editMode")) {
            x.addEventListener("click", evt => {
                if (
                    evt.target.parentElement.classList.toggle("correct-answer")
                );
            });
        } else {
            x.parentElement.addEventListener("click", evt => {
                const classes = evt.currentTarget.classList;
                classes.add("userSelected");
                const correct = classes.contains("correct-answer");
                const soundUrl = correct
                    ? "right_answer.mp3"
                    : "wrong_answer.mp3";
                playSound(soundUrl);
                // Make the state of the hidden input conform (for screen readers). Only if the
                // correct answer was clicked does the checkbox get checked.
                const checkBox = evt.currentTarget.getElementsByClassName(
                    "hiddenCheckbox"
                )[0];
                if (checkBox) {
                    checkBox.checked = correct;
                }

                // This might cause us to send analytics information...tell the app if it's interested.
                if (window.analyticsChange) {
                    window.analyticsChange();
                }
            });
        }
    }
}

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

function playSound(url) {
    const player = getPagePlayer();
    player.setAttribute("src", url);
    player.play();
}

function getPagePlayer() {
    let player = document.querySelector("#quiz-sound-player");
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
    var answers = document.getElementsByClassName("check-answer-wrapper");
    for (var i = 0; i < answers.length; i++) {
        if (hasContent(answers[i])) {
            answers[i].classList.remove("empty");
        } else {
            answers[i].classList.add("empty");
        }
    }
}

function hasContent(answer) {
    var editables = answer.getElementsByClassName("bloom-editable");
    for (var j = 0; j < editables.length; j++) {
        var editable = editables[j];
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
