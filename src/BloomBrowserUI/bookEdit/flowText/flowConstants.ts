// Attribute and class names shared by the flow-text modules. Kept dependency-free so that
// every other module in bookEdit/flowText can import it.

// Goes on a translation group whose text continues into another group. Every group of one
// chain carries the same value, a guid made when the chain is first created.
export const kFlowChainAttr = "data-flow-chain";
export const kChainedGroupSelector = `.bloom-translationGroup[${kFlowChainAttr}]`;

// Goes on the FIRST <p> of a bloom-editable when that paragraph is the tail of a paragraph
// that starts in the previous box of the chain. It is content markup: it is saved with the
// page, and it is what tells us to join the two halves back together before we re-fit.
// Only the first <p> of an editable may carry it.
export const kContinuationAttr = "data-flow-continuation";
export const kContinuationAttrValue = "true";

// Transient classes and attributes: the editing code puts them on, and removeEditingDebris
// takes them off again, so they never reach the saved page.

// On a chained translation group that has, or is, more text in a following box.
export const kHasNextClass = "bloom-flow-hasNext";
export const kHasPrevClass = "bloom-flow-hasPrev";

// On a chained translation group whose content flow cannot handle (see flowChain), with the
// reason in the attribute so the user interface can explain itself.
export const kRefusedClass = "bloom-flow-refused";
export const kRefusedReasonAttr = "data-flow-refused-reason";

// On a translation group while a pass is moving text through it. Handlers that would
// otherwise react to the mutations must ignore a group that carries this.
export const kReflowingAttr = "data-flow-reflowing";
