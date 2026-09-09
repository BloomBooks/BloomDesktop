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

// Marks the character at which the text of a box stops fitting in it. OverflowChecker puts
// it in whenever a normal-style box overflows, whether or not the box is part of a chain,
// and takes it out again when the box fits. It is content markup: it is saved with the page,
// so C# can split the paragraph there without measuring anything.
export const kOverflowStartClass = "bloom-overflowStart";
export const kOverflowMarkerSelector = `span.${kOverflowStartClass}`;

// U+200C ZERO WIDTH NON-JOINER, the same character Bloom's soft line break puts next to its
// own span. The marker has to hold a character: an inline element with nothing in it is
// pruned by our own normalization and by CKEditor. This one is the safe choice, because
// EditableDivUtils.removeCkEditorFillingChars deliberately spares U+200C while it strips
// U+200B, and because U+FEFF would act as a word joiner and stop the line breaking at the
// one place we want it to break.
export const kOverflowMarkerContent = String.fromCharCode(0x200c);

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

// The button an empty box shows to offer to take the text that does not fit in an earlier
// box. It is a bloom-ui element, so HtmlDom takes it out of the page Bloom saves.
export const kContinueButtonClass = "bloom-flow-continue";
export const kContinueButtonTestId = "flow-text-continue";
export const kContinueButtonL10nId = "EditTab.FlowText.ContinueFromBoxAbove";
export const kContinueButtonEnglish = "Continue text from the box above";
