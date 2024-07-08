// This file contains code adapted from BloomPlayer's narration.ts for playing audio
// in a draggable page.
// It is quite difficult to know how to handle audio in a drag activity page.
// We need to be able to play it both during "Play" and when showing the page in BP.
// There is existing code for playing audio in both Bloom Desktop and Bloom Player.
// The Bloom Desktop code in audioRecording.ts is entnagled with the code that manages
// audio recording and the talking book tool.
// The Bloom Player code in narration.ts is entangled with the code that manages
// autoplay and page advance, and also handles cases where the text being played is
// not fully visible. It deals with pause and continue, which also interact with
// the Bloom Player controls and with video and background music.
// On the other hand, it assumes everything on the page will be played in succession,
// which is not appropriate for a drag activity page.
// At this stage, when a bloom game has both narration and video
// that should autoplay at the same time, the video plays first. I have not tried to handle pause/resume.
// So this file contains just enough code to get the right sounds to play at the
// right times. The other cases will be handled later.

// Code here may not need a comprehensive review, since it started as a copy of narration.ts.
// playAllVideo is new, and blocks commented out may deserve a look.
// Methods related to urlPrefix have had some changes.
// The hope is that we will somehow merge this code with the existing Bloom Player code
// and perhaps also remove some duplication with audioRecording.ts; that may be a better time
// for careful review.

let currentPlayPage: HTMLElement | null = null;

let playerUrlPrefix = "";
// In bloom player, figuring the url prefix (to put before file JS needs to locate, like
// sounds to play) is complicated. We pass it in.
// In Bloom desktop, it's almost more tricky. If we're executing in the page iframe, we can
// easily derive it from the page's URL. But if we're executing in the toolbox, that doesn't work.
// So we arrange for newPageReady, called at least as often as changing book, to set it up
// using setPlayerUrlPrefixFromWindowLocationHref
export function setPlayerUrlPrefix(prefix: string) {
    playerUrlPrefix = prefix;
}

export function setPlayerUrlPrefixFromWindowLocationHref(bookSrc: string) {
    setPlayerUrlPrefix(getUrlPrefixFromWindowHref(bookSrc));
}

function getUrlPrefixFromWindowHref(bookSrc: string) {
    const index = bookSrc.lastIndexOf("/");
    return bookSrc.substring(0, index);
}

export function urlPrefix(): string {
    if (playerUrlPrefix) {
        return playerUrlPrefix;
    }
    return getUrlPrefixFromWindowHref(window.location.href);
}

function sortAudioElements(input: HTMLElement[]): HTMLElement[] {
    const keyedItems = input.map((item, index) => {
        return { tabindex: getTgTabIndex(item), index, item };
    });
    keyedItems.sort((x, y) => {
        // If either is not in a translation group with a tabindex,
        // order is determined by their original index.
        // Likewise if the tabindexes are the same.
        if (!x.tabindex || !y.tabindex || x.tabindex === y.tabindex) {
            return x.index - y.index;
        }
        // Otherwise, determined by the numerical order of tab indexes.
        return parseInt(x.tabindex, 10) - parseInt(y.tabindex, 10);
    });
    return keyedItems.map(x => x.item);
}

function getTgTabIndex(input: HTMLElement): string | null {
    let tg: HTMLElement | null = input;
    while (tg && !tg.classList.contains("bloom-translationGroup")) {
        tg = tg.parentElement;
    }
    if (!tg) {
        return "999";
    }
    return tg.getAttribute("tabindex") || "999";
}

const kSegmentClass = "bloom-highlightSegment";
export interface ISetHighlightParams {
    newElement: Element;
    shouldScrollToElement: boolean;
    disableHighlightIfNoAudio?: boolean;
    oldElement?: Element | null | undefined; // Optional. Provides some minor optimization if set.
}

// Indicates that highlighting is briefly/temporarily suppressed,
// but may become highlighted later.
// For example, audio highlighting is suppressed until the related audio starts playing (to avoid flashes)
const kSuppressHighlightClass = "ui-suppressHighlight";

// Even though these can now encompass more than strict sentences,
// we continue to use this class name for backwards compatability reasons.
export const kAudioSentence = "audio-sentence";

const kImageDescriptionClass = "bloom-imageDescription";

// Unused in Bloom desktop, but in Bloom player, current page might change while a series of sounds
// is playing. This lets us avoid starting the next sound if the page has changed in the meantime.
export function setCurrentPage(page: HTMLElement) {
    currentPlayPage = page;
}

let currentAudioId = "";

let elementsToPlayConsecutivelyStack: HTMLElement[] = [];
let subElementsWithTimings: Array<[Element, number]> = [];

export enum PlaybackMode {
    NewPage, // starting a new page ready to play
    NewPageMediaPaused, // starting a new page in the "paused" state
    VideoPlaying, // video is playing
    VideoPaused, // video is paused
    AudioPlaying, // narration and/or animation are playing (or possibly finished)
    AudioPaused, // narration and/or animation are paused
    MediaFinished // video, narration, and/or animation has played (possibly no media to play)
    // Note that music can be playing when the state is either AudioPlaying or MediaFinished.
}

let currentPlaybackMode: PlaybackMode = PlaybackMode.NewPage;

// A Session Number that keeps track of each time playAllAudio started.
// This might be needed to keep track of changing pages, or when we start new audio
// that will replace something already playing.
let currentAudioSessionNum: number = 0;

// This represents the start time of the current playing of the audio. If the user presses pause/play, it will be reset.
// This is used for analytics reporting purposes
let audioPlayCurrentStartTime: number | null = null; // milliseconds (since 1970/01/01, from new Date().getTime())

// Our current notion of current page is based on the page of the list of elements that
function getCurrentPage(): HTMLElement {
    return currentPlayPage!;
}

function playCurrentInternal() {
    if (currentPlaybackMode === PlaybackMode.AudioPlaying) {
        const mediaPlayer = getPlayer();
        if (mediaPlayer) {
            const element = getCurrentPage().querySelector(
                `#${currentAudioId}`
            );
            if (!element || !canPlayAudio(element)) {
                playEnded();
                return;
            }

            // Regardless of whether we end up using timingsStr or not,
            // we should reset this now in case the previous page used it and was still playing
            // when the user flipped to the next page.
            subElementsWithTimings = [];

            const timingsStr: string | null = element.getAttribute(
                "data-audioRecordingEndTimes"
            );
            if (timingsStr) {
                const childSpanElements = element.querySelectorAll(
                    `span.${kSegmentClass}`
                );
                const fields = timingsStr.split(" ");
                const subElementCount = Math.min(
                    fields.length,
                    childSpanElements.length
                );

                for (let i = subElementCount - 1; i >= 0; --i) {
                    const durationSecs: number = Number(fields[i]);
                    if (isNaN(durationSecs)) {
                        continue;
                    }
                    subElementsWithTimings.push([
                        childSpanElements.item(i),
                        durationSecs
                    ]);
                }
            } else {
                // No timings string available.
                // No need for us to do anything. The correct element is already highlighted by playAllSentences() (which needed to call setCurrent... anyway to set the audio player source).
                // We'll just proceed along, start playing the audio, and playNextSubElement() will return immediately because there are no sub-elements in this case.
            }

            const currentSegment = element as HTMLElement;
            // if (currentSegment && ToggleImageDescription) {
            //     ToggleImageDescription.raise(
            //         isImageDescriptionSegment(currentSegment)
            //     );
            // }

            gotErrorPlaying = false;
            console.log("playing " + currentAudioId + "..." + mediaPlayer.src);
            const promise = mediaPlayer.play();
            ++currentAudioSessionNum;
            audioPlayCurrentStartTime = new Date().getTime();
            highlightNextSubElement(currentAudioSessionNum);
            handlePlayPromise(promise);
        }
    }
}

function handlePlayPromise(promise: Promise<void>) {
    // In newer browsers, play() returns a promise which fails
    // if the browser disobeys the command to play, as some do
    // if the user hasn't 'interacted' with the page in some
    // way that makes the browser think they are OK with it
    // playing audio. In Gecko45, the return value is undefined,
    // so we mustn't call catch.
    if (promise && promise.catch) {
        promise.catch((reason: any) => {
            // There is an error handler here, but the HTMLMediaElement also has an error handler (which may end up calling playEnded()).
            // In case it doesn't, we make sure here that it happens
            handlePlayError();
            // This promise.catch error handler is the only one that handles NotAllowedException (that is, playback not started because user has not interacted with the page yet).
            // However, older versions of browsers don't support promise from HTMLMediaElement.play(). So this cannot be the only error handler.
            // Thus we need both the promise.catch error handler as well as the HTMLMediaElement's error handler.
            //
            // In many cases (such as NotSupportedError, which happens when the audio file isn't found), both error handlers will run.
            // That is a little annoying but if the two don't conflict with each other it's not problematic.

            console.log("could not play sound: " + reason);

            if (
                reason &&
                reason
                    .toString()
                    .includes(
                        "The play() request was interrupted by a call to pause()."
                    )
            ) {
                // We were getting this error Aug 2020. I tried wrapping the line above which calls mediaPlayer.play()
                // (currently `promise = mediaPlayer.play();`) in a setTimeout with 0ms. This seemed to fix the bug (with
                // landscape books not having audio play initially -- BL-8887). But the root cause was actually that
                // we ended up calling playAllSentences twice when the book first loaded.
                // I fixed that in bloom-player-core. But I wanted to document the possible setTimeout fix here
                // in case this issue ever comes up for a different reason.
                console.log(
                    "See comment in narration.ts for possibly useful information regarding this error."
                );
            }

            // Don't call removeAudioCurrent() here. The HTMLMediaElement's error handler will call playEnded() and calling removeAudioCurrent() here will mess up playEnded().
            // removeAudioCurrent();

            // With some kinds of invalid sound file it keeps trying and plays over and over.
            // But when we move on to play another sound, a pause here will mess things up.
            // So instead I put a pause after we run out of sounds to try to play.
            //getPlayer().pause();
            // if (Pause) {
            //     Pause.raise();
            // }

            // Get all the state (and UI) set correctly again.
            // Not entirely sure about limiting this to NotAllowedError, but that's
            // the one kind of play error that is fixed by the user just interacting.
            // If there's some other reason we can't play, showing as paused may not
            // be useful. See comments on the similar code in music.ts
            // if (reason.name === "NotAllowedError" && PlayFailed) {
            //     PlayFailed.raise();
            // }
        });
    }
}

// Moves the highlight to the next sub-element
// originalSessionNum: The value of currentAudioSessionNum at the time when the audio file started playing.
// This is used to check in the future if the timeouts we started are for the right session
// startTimeInSecs is an optional fallback that will be used in case the currentTime cannot be determined from the audio player element.
function highlightNextSubElement(
    originalSessionNum: number,
    startTimeInSecs: number = 0
) {
    // the item should not be popped off the stack until it's completely done with.
    const subElementCount = subElementsWithTimings.length;

    if (subElementCount <= 0) {
        return;
    }

    const topTuple = subElementsWithTimings[subElementCount - 1];
    const element = topTuple[0];
    const endTimeInSecs: number = topTuple[1];

    setHighlightTo({
        newElement: element,
        shouldScrollToElement: true,
        disableHighlightIfNoAudio: false
    });

    const mediaPlayer: HTMLMediaElement = document.getElementById(
        "bloom-audio-player"
    )! as HTMLMediaElement;
    let currentTimeInSecs: number = mediaPlayer.currentTime;
    if (currentTimeInSecs <= 0) {
        currentTimeInSecs = startTimeInSecs;
    }

    // Handle cases where the currentTime has already exceeded the nextStartTime
    //   (might happen if you're unlucky in the thread queue... or if in debugger, etc.)
    // But instead of setting time to 0, set the minimum highlight time threshold to 0.1 (this threshold is arbitrary).
    const durationInSecs = Math.max(endTimeInSecs - currentTimeInSecs, 0.1);

    setTimeout(() => {
        onSubElementHighlightTimeEnded(originalSessionNum);
    }, durationInSecs * 1000);
}

// Handles a timeout indicating that the expected time for highlighting the current subElement has ended.
// If we've really played to the end of that subElement, highlight the next one (if any).
// originalSessionNum: The value of currentAudioSessionNum at the time when the audio file started playing.
//     This is used to check in the future if the timeouts we started are for the right session
function onSubElementHighlightTimeEnded(originalSessionNum: number) {
    // Check if the user has changed pages since the original audio for this started playing.
    // Note: Using the timestamp allows us to detect switching to the next page and then back to this page.
    //       Using playerPage (HTMLElement) does not detect that.
    if (originalSessionNum !== currentAudioSessionNum) {
        return;
    }
    // Seems to be needed to prevent jumping to the next subelement when not permitted to play by browser.
    // Not sure why the check below on mediaPlayer.currentTime does not prevent this.
    if (currentPlaybackMode === PlaybackMode.AudioPaused) {
        return;
    }

    const subElementCount = subElementsWithTimings.length;
    if (subElementCount <= 0) {
        return;
    }

    const mediaPlayer: HTMLMediaElement = document.getElementById(
        "bloom-audio-player"
    )! as HTMLMediaElement;
    if (mediaPlayer.ended || mediaPlayer.error) {
        // audio playback ended. No need to highlight anything else.
        // (No real need to remove the highlights either, because playEnded() is supposed to take care of that.)
        return;
    }
    const playedDurationInSecs: number | undefined | null =
        mediaPlayer.currentTime;

    // Peek at the next sentence and see if we're ready to start that one. (We might not be ready to play the next audio if the current audio got paused).
    const subElementWithTiming = subElementsWithTimings[subElementCount - 1];
    const nextStartTimeInSecs = subElementWithTiming[1];

    if (playedDurationInSecs && playedDurationInSecs < nextStartTimeInSecs) {
        // Still need to wait. Exit this function early and re-check later.
        const minRemainingDurationInSecs =
            nextStartTimeInSecs - playedDurationInSecs;
        setTimeout(() => {
            onSubElementHighlightTimeEnded(originalSessionNum);
        }, minRemainingDurationInSecs * 1000);

        return;
    }

    subElementsWithTimings.pop();

    highlightNextSubElement(originalSessionNum, nextStartTimeInSecs);
}

function removeAudioCurrent(around: HTMLElement = document.body) {
    // Note that HTMLCollectionOf's length can change if you change the number of elements matching the selector.
    // For safety we get rid of all existing ones. But we do take a starting point element
    // (might be the one that has the higlight, or the one getting it)
    // to make sure we're cleaning up in the right document, which is in question when used in
    // Bloom Editor.
    const audioCurrentArray = Array.from(
        around.ownerDocument.getElementsByClassName("ui-audioCurrent")
    );

    for (let i = 0; i < audioCurrentArray.length; i++) {
        audioCurrentArray[i].classList.remove("ui-audioCurrent");
    }
    const currentImg = document.getElementsByClassName("ui-audioCurrentImg")[0];
    if (currentImg) {
        currentImg.classList.remove("ui-audioCurrentImg");
    }
}

function setSoundAndHighlight(
    newElement: Element,
    disableHighlightIfNoAudio: boolean,
    oldElement?: Element | null | undefined
) {
    setHighlightTo({
        newElement,
        shouldScrollToElement: true, // Always true in bloom-player version
        disableHighlightIfNoAudio,
        oldElement
    });
    setSoundFrom(newElement);
}

function setHighlightTo({
    newElement,
    shouldScrollToElement,
    disableHighlightIfNoAudio,
    oldElement
}: ISetHighlightParams) {
    // This should happen even if oldElement and newElement are the same.
    // But I don't expect drag activity pages to have text that needs scrolling to view it, so leaving out for now.
    // if (shouldScrollToElement) {
    //     // Wrap it in a try/catch so that if something breaks with this minor/nice-to-have feature of scrolling,
    //     // the main responsibilities of this method can still proceed
    //     try {
    //         scrollElementIntoView(newElement);
    //     } catch (e) {
    //         console.error(e);
    //     }
    // }

    if (oldElement === newElement) {
        // No need to do much, and better not to, so that we can avoid any temporary flashes as the highlight is removed and re-applied
        return;
    }

    removeAudioCurrent((oldElement || newElement) as HTMLElement);

    if (disableHighlightIfNoAudio) {
        const mediaPlayer = getPlayer();
        const isAlreadyPlaying = mediaPlayer.currentTime > 0;

        // If it's already playing, no need to disable (Especially in the Soft Split case, where only one file is playing but multiple sentences need to be highlighted).
        if (!isAlreadyPlaying) {
            // Start off in a highlight-disabled state so we don't display any momentary highlight for cases where there is no audio for this element.
            // In react-based bloom-player, canPlayAudio() can't trivially identify whether or not audio exists,
            // so we need to incorporate a derivative of Bloom Desktop's .ui-suppressHighlight code
            newElement.classList.add(kSuppressHighlightClass);
            mediaPlayer.addEventListener("playing", () => {
                newElement.classList.remove(kSuppressHighlightClass);
            });
        }
    }

    newElement.classList.add("ui-audioCurrent");
    // If the current audio is part of a (currently typically hidden) image description,
    // highlight the image.
    // it's important to check for imageDescription on the translationGroup;
    // we don't want to highlight the image while, for example, playing a TOP box content.
    const translationGroup = newElement.closest(".bloom-translationGroup");
    if (
        translationGroup &&
        translationGroup.classList.contains(kImageDescriptionClass)
    ) {
        const imgContainer = translationGroup.closest(".bloom-imageContainer");
        if (imgContainer) {
            imgContainer.classList.add("ui-audioCurrentImg");
        }
    }
}

function setSoundFrom(element: Element) {
    const firstAudioSentence = getFirstAudioSentenceWithinElement(element);
    const id: string = firstAudioSentence ? firstAudioSentence.id : element.id;
    setCurrentAudioId(id);
}

function getFirstAudioSentenceWithinElement(
    element: Element | null
): Element | null {
    const audioSentences = getAudioSegmentsWithinElement(element);
    if (!audioSentences || audioSentences.length === 0) {
        return null;
    }

    return audioSentences[0];
}

function getAudioSegmentsWithinElement(element: Element | null): Element[] {
    const audioSegments: Element[] = [];

    if (element) {
        if (element.classList.contains(kAudioSentence)) {
            audioSegments.push(element);
        } else {
            const collection = element.getElementsByClassName(kAudioSentence);
            for (let i = 0; i < collection.length; ++i) {
                const audioSentenceElement = collection.item(i);
                if (audioSentenceElement) {
                    audioSegments.push(audioSentenceElement);
                }
            }
        }
    }

    return audioSegments;
}

function setCurrentAudioId(id: string) {
    if (!currentAudioId || currentAudioId !== id) {
        currentAudioId = id;
        updatePlayerStatus();
    }
}

function updatePlayerStatus() {
    const player = getPlayer();
    if (!player) {
        return;
    }
    // Any time we change the src, the player will pause.
    // So if we're playing currently, we'd better report whatever time
    // we played.
    // if (player.currentTime > 0 && !player.paused && !player.ended) {
    //     reportPlayDuration();
    // }
    const url = currentAudioUrl(currentAudioId);
    //logSound(url, 1);
    // because this code is meant to work in both Bloom and BloomPlayer, we can't call a Bloom API to find
    // out whether we actually have a recording (as we well might not, if we just opened the talking book
    // tool and haven't recorded anything yet). So we just try to play it and see what happens.
    // The optional param tells Bloom not to report an error if the file isn't found.
    player.setAttribute(
        "src",
        url + "?nocache=" + new Date().getTime() + "&optional=true"
    );
}

function currentAudioUrl(id: string): string {
    const result = urlPrefix() + "/audio/" + id + ".mp3";
    console.log("trying to play " + result);
    return result;
}

function getPlayer(): HTMLMediaElement {
    const audio = getAudio("bloom-audio-player", _ => {});
    // We used to do this in the init call, but sometimes the function didn't get called.
    // Suspecting that there are cases, maybe just in storybook, where a new instance
    // of the narration object gets created, but the old audio element still exists.
    // Make sure the current instance has our end function.
    // Because it is a fixed function for the lifetime of this object, addEventListener
    // will not add it repeatedly.
    audio.addEventListener("ended", playEnded);
    audio.addEventListener("error", handlePlayError);
    return audio;
}

function playEnded(): void {
    // Not sure if this is necessary, since both 'playCurrentInternal()' and 'reportPlayEnded()'
    // will toggle image description already, but if we've just gotten to the end of our "stack",
    // it may be needed.
    // Not even sure image descriptions make sense for drag activities.
    // if (ToggleImageDescription) {
    //     ToggleImageDescription.raise(false);
    // }
    // Do we want this for drag activity narration?
    //reportPlayDuration();
    if (
        elementsToPlayConsecutivelyStack &&
        elementsToPlayConsecutivelyStack.length > 0
    ) {
        const elementJustPlayed = elementsToPlayConsecutivelyStack.pop(); // get rid of the last one we played
        const newStackCount = elementsToPlayConsecutivelyStack.length;
        if (newStackCount > 0) {
            // More items to play
            const nextElement =
                elementsToPlayConsecutivelyStack[newStackCount - 1];
            setSoundAndHighlight(nextElement, true);
            playCurrentInternal();
        } else {
            // Nothing left to play. Anything we should do?
            //reportPlayEnded();
            removeAudioCurrent(elementJustPlayed);
            // In some error conditions, we need to stop repeating attempts to play.
            getPlayer().pause();
        }
    }
}

function getAudio(id: string, init: (audio: HTMLAudioElement) => void) {
    let player: HTMLAudioElement | null = document.querySelector(
        "#" + id
    ) as HTMLAudioElement;
    if (player && !player.play) {
        player.remove();
        player = null;
    }
    if (!player) {
        player = document.createElement("audio") as HTMLAudioElement;
        player.setAttribute("id", id);
        document.body.appendChild(player);
        init(player);
    }
    return player as HTMLMediaElement;
}

function canPlayAudio(current: Element): boolean {
    return true; // currently no way to check
}

// If something goes wrong playing a media element, typically that we don't actually have a recording
// for a particular one, we seem to sometimes get an error event, while other times, the promise returned
// by play() is rejected. Both cases call handlePlayError, which calls playEnded, but in case we get both,
// we don't want to call playEnded twice.
let gotErrorPlaying = false;

function handlePlayError() {
    if (gotErrorPlaying) {
        console.log("Already got error playing, not handling again");
        return;
    }
    gotErrorPlaying = true;
    console.log("Error playing, handling");
    setTimeout(() => {
        playEnded();
    }, 100);
}

//------ code used only for drag activities
export function playAllAudio(elements: HTMLElement[]): void {
    console.log("playAllAudio " + elements.length);
    currentPlayPage = elements[0]?.closest(".bloom-page") as HTMLElement;
    const mediaPlayer = getPlayer();
    if (mediaPlayer) {
        //mediaPlayer.pause();
        mediaPlayer.currentTime = 0;
    }

    // Invalidate old ID, even if there's no new audio to play.
    // (Deals with the case where you are on a page with audio, switch to a page without audio, then switch back to original page)
    ++currentAudioSessionNum;

    // I think this is for big blocks of text with long runs of spaces for alignment. Don't expect this in drag activities.
    //fixHighlighting();

    // Sorted into the order we want to play them, then reversed so we
    // can more conveniently pop the next one to play from the end of the stack.
    elementsToPlayConsecutivelyStack = sortAudioElements(elements).reverse();

    const stackSize = elementsToPlayConsecutivelyStack.length;
    if (stackSize === 0) {
        // Nothing to play. Wait the standard amount of time anyway, in case we're autoadvancing.
        // Don't think we need this here. We don't auto-advance from a game page.
        // if (PageNarrationComplete) {
        //     pageNarrationCompleteTimer = window.setTimeout(() => {
        //         PageNarrationComplete.raise();
        //     }, durationOfPagesWithoutNarration * 1000);
        // }

        // Less sure about this. We may eventually need to know audio finished (e.g., to start video)
        // if (PlayCompleted) {
        //     PlayCompleted.raise();
        // }
        return;
    }

    const firstElementToPlay = elementsToPlayConsecutivelyStack[stackSize - 1]; // Remember to pop it when you're done playing it. (i.e., in playEnded)

    setSoundAndHighlight(firstElementToPlay, true);
    // Currently this is required for playCurrentInternal to actually play the sound.
    // We are not currently fully maintaining this state.
    currentPlaybackMode = PlaybackMode.AudioPlaying;
    playCurrentInternal();
    return;
}

// Play the specified elements, one after the other. When the last completes (or at once if the array is empty),
// perform the 'then' action (typically used to play narration, which we put after videos).
// Todo: Bloom Player version, at least, should work with play/pause/resume/change page architecture.
export function playAllVideo(elements: HTMLVideoElement[], then: () => void) {
    if (elements.length === 0) {
        then();
        return;
    }
    const video = elements[0];
    // Note: sometimes this event does not fire normally, even when the video is played to the end.
    // I have not figured out why. It may be something to do with how we are trimming them.
    // In Bloom, this is worked around by raising the ended event when we detect that it has paused past the end point
    // in resetToStartAfterPlayingToEndPoint.
    // In BloomPlayer, we may need to do something similar.
    video.addEventListener(
        "ended",
        () => {
            playAllVideo(elements.slice(1), then);
        },
        { once: true }
    );
    video.play();
}
