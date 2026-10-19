import { copyContentToTarget, getTarget } from "bloom-player";
import { doOutsideHistory } from "../pageHistory/pageHistoryAccess";

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
//
// The target's content is a copy of the draggable's, so the page history leaves it out: every copy
// runs outside the history, and after each undo and redo the Game tool copies every draggable on
// the page again (copyContentToTargetsAfterUndoOrRedo in GameTool.tsx). Recording a copy would not
// work. copyContentToTarget compares the whole innerHTML, including markup that exists only while
// editing (CKEditor's classes, for instance), so a copy made after an undo can differ from the
// target that the undo put back, and would join the latest undo step and clear redo. And the
// history compares a recorded child list by node identity, so once one copy is not recorded, a step
// that recorded the target's children would be refused. Every copy must therefore come through here.
export function copyContentToTargetAndCleanup(
    draggableElement: HTMLElement,
): void {
    doOutsideHistory(() => copyContentToTarget(draggableElement));
}
