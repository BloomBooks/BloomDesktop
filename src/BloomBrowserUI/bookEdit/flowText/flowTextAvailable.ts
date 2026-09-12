// Whether this collection may use flow text at all.
//
// Flow text is the FlowText feature in Bloom's feature registry: it needs a subscription of any
// paid tier, and it needs the flow-text experimental feature turned on in the Advanced tab of
// the collection Settings dialog. The features/status api answers both at once, as the enabled
// and visible flags of a feature status.
//
// Every affordance the flow offers asks here first, and so does the pass that moves text
// between boxes. FlowTextApi refuses the same requests on its own account, so a page that
// somehow got this wrong cannot change a book.
//
// This module asks the api directly rather than through react_components/featureStatus, whose
// helpers are React hooks: flowIndicators is in OverflowChecker's import graph and imports
// this, and React has no business being pulled in there.

import { getAsync } from "../../utils/bloomApi";

// Must match the FlowText member of the c# FeatureName enum (FeatureRegistry.cs).
const kFeatureName = "FlowText";

let available: boolean | undefined;
let asking: Promise<boolean> | undefined;

/**
 * Whether Bloom may offer flow text. False until the answer has come from Bloom, so a caller
 * that must not act early asks isFlowTextAvailabilityKnown, or waits for
 * whenFlowTextAvailabilityKnown.
 */
export function isFlowTextAvailable(): boolean {
    return available === true;
}

/** Has Bloom answered yet? */
export function isFlowTextAvailabilityKnown(): boolean {
    return available !== undefined;
}

/**
 * Settles once Bloom has said whether this collection may use flow text. One request serves
 * every caller, and the answer is kept: a subscription entered, or the experimental feature
 * turned on, reaches the page after next rather than this one.
 */
export function whenFlowTextAvailabilityKnown(): Promise<boolean> {
    if (available !== undefined) {
        return Promise.resolve(available);
    }

    if (!asking) {
        asking = getAsync(
            `features/status?featureName=${kFeatureName}&forPublishing=false`,
        ).then((result) => {
            const status = result?.data;
            available = status?.enabled === true && status?.visible === true;
            return available;
        });
    }

    return asking;
}

/**
 * Say what the answer is without asking Bloom. Tests have no Bloom to ask; passing undefined
 * puts the module back to knowing nothing.
 */
export function setFlowTextAvailableForTesting(
    value: boolean | undefined,
): void {
    available = value;
    asking = undefined;
}
