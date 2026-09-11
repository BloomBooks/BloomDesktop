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

// Goes beside the continuation attribute when the text was cut at a space, and says that the
// join has to put that space back.
//
// The space itself cannot be stored: no whitespace survives at the edge of a paragraph. The
// editor writes a paragraph's trailing space as a zero-width filler, and a space at the head of
// a paragraph does not come back from the saved page. So the two halves of a cut paragraph hold
// no whitespace at the cut, and this attribute is what says a space belongs there. Nothing needs
// it outside the editor: in the book each half is a paragraph of its own text box, and neither
// wants a space at its edge.
export const kSeamSpaceAttr = "data-flow-seam-space";
export const kSeamSpaceAttrValue = "true";

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
export const kContinueButtonEnglish = "flow text here from the box above";

// The same offer, when the box whose text does not fit is on an earlier page. {0} is what the
// reader calls that page, which C# works out (FlowTextChains.FindPendingOverflowBefore).
export const kContinueFromPageL10nId = "EditTab.FlowText.ContinueFromPage";
export const kContinueFromPageEnglish = "flow text here from page {0}";

// The label a box wears when its text continues from a linked box on an EARLIER PAGE. A box
// whose previous linked box is on this page carries kHasPrevClass instead, which the reader can
// see for themselves. It is a bloom-ui element, so HtmlDom takes it out of the page Bloom saves.
// {0} is what the reader calls the earlier page, which C# works out (FlowTextApi previous).
//
// The arrow the label starts with is drawn by editMode.less, so that it is not part of anything a
// translator is given and cannot be lost or reordered in a translation.
export const kFlowFromClass = "bloom-flow-from";
export const kFlowFromTestId = "flow-text-flows-from";
export const kFlowFromPageL10nId = "EditTab.FlowText.FlowsFromPage";
export const kFlowFromPageEnglish = "flows from page {0}";
export const kFlowFromPreviousPageL10nId =
    "EditTab.FlowText.FlowsFromPreviousPage";
export const kFlowFromPreviousPageEnglish = "flows from the previous page";

// The label a box wears when its text continues into a linked box on a LATER PAGE. A box whose
// next linked box is on this page carries kHasNextClass instead, whose arrow the reader can
// follow to the box below. It is a bloom-ui element, so HtmlDom takes it out of the page Bloom
// saves. {0} is what the reader calls the later page, which C# works out (FlowTextApi peekNext).
//
// As with the label above, the arrow it ends with is drawn by editMode.less.
export const kFlowToClass = "bloom-flow-to";
export const kFlowToTestId = "flow-text-flows-to";
export const kFlowToPageL10nId = "EditTab.FlowText.FlowsToPage";
export const kFlowToPageEnglish = "flows to page {0}";
export const kFlowToNextPageL10nId = "EditTab.FlowText.FlowsToNextPage";
export const kFlowToNextPageEnglish = "flows to the next page";

// The button the LAST box of a flow offers when its text still does not fit and there is no box
// after it to take the rest: it makes the pages the rest of the text needs and flows into them.
// It is a bloom-ui element, so HtmlDom takes it out of the page Bloom saves.
export const kCreatePagesButtonClass = "bloom-flow-createPages";
export const kCreatePagesButtonTestId = "flow-text-create-pages";
export const kCreatePagesButtonL10nId =
    "EditTab.FlowText.CreatePagesAndContinue";
export const kCreatePagesButtonEnglish = "Create pages and flow text";

// The panel a chained group wears, beside the page, while a refit of its chain waits to be run
// (flowReflowBubble.tsx). It is a bloom-ui element, so HtmlDom takes it out of the page Bloom
// saves.
export const kReflowBubbleClass = "bloom-flow-reflowBubble";
export const kReflowBubbleTestId = "flow-reflow-bubble";
export const kReflowPendingL10nId = "EditTab.FlowText.ReflowPending";
export const kReflowPendingEnglish = "Reflow pending";
export const kReflowOnPageChangeTestId = "flow-reflow-on-page-change";
export const kReflowOnPageChangeL10nId = "EditTab.FlowText.ReflowOnPageChange";
export const kReflowOnPageChangeEnglish = "Reflow when you change pages";
// Whether a refit may change how many pages the book has: with it on, a refit adds text-only
// pages when the run does not fit its last box, and removes the pages the run leaves empty.
// With it off, a refit only divides the run up over the pages that are already there.
export const kAutoPagesTestId = "flow-reflow-auto-pages";
export const kAutoPagesL10nId = "EditTab.FlowText.AutoPages";
export const kAutoPagesEnglish = "Automatically add & remove pages";
export const kReflowNowTestId = "flow-reflow-now";
export const kReflowNowL10nId = "EditTab.FlowText.ReflowNow";
export const kReflowNowEnglish = "Reflow now";

// Set on the body of a page C# has loaded into a browser nobody is looking at, only to ask
// where one box's text stops fitting (FlowTextWalk.AskBrowserForFit, captureFlowFit). Such a
// page holds the whole of the run that is left in one box, which is more text than fits it on
// purpose, so the passes that settle a page must not run: they would move that text out of the
// box, and off-screen there is nowhere for it to go.
export const kMeasuringFlowFitAttr = "data-bloom-measuring-flow-fit";
