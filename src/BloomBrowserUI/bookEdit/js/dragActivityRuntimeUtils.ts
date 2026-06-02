import { copyContentToTarget, getTarget } from "bloom-player";

// This function was made with the intent of allowing us to clean up the target after we copy it.
// That is not a good idea, because it defeats code in copyContentToTarget that is designed to
// prevent flicker by not actually setting the target's innerHTML unless it would change.
// If we change it after the fact, then it will be different from what copyContentToTarget
// calculates, and there will be two unwanted DOM changes, once inside that function, and probably
// another one when we clean it up again.
// I've kept this function because it's somewhat useful in debugging to have a single place to
// set a breakpoint for calls to copyContentToTarget, but it should not be used to modify the target.
// Also, it provides a good place to document why we should not reinvent the idea of cleanup
// after we copyContentToTarget. The logic for what not to copy has to be in the function itself,
// even though that inconveniently requires a new version of bloom-player.
// If that becomes too much of a nuisance, it may be helpful to add a callback parameter to
// copyContentToTarget that can be used to clean up the throwaway object that is used to calculate
// the new innerHTML.
export function copyContentToTargetAndCleanup(
    draggableElement: HTMLElement,
): void {
    copyContentToTarget(draggableElement);
}
