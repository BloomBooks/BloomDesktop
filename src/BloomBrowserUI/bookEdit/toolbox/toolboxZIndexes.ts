// The one place that defines how the Talking Book tool's "Show Playback Order" disabling
// overlay layers against the rest of the toolbox. Both ends of the relationship import from
// here so they cannot drift apart:
//   - the overlay itself, in talkingBook/TalkingBookToolControls.tsx
//   - the tool headers that have to stay above it, in ToolboxRoot.tsx
//
// Until 6.4 the header side of this was the "#toolbox h3 { z-index: 1005 }" rule in
// toolbox.less. The headers became MUI AccordionSummary elements when the toolbox root was
// converted to React, that rule quietly stopped matching them, and they started being painted
// over by the overlay -- BL-16630. (The rule is still in toolbox.less but matches nothing now,
// since the legacy #toolbox no longer holds the headers.)

// While Show Playback Order mode is on, the Talking Book tool covers the whole toolbox with a
// translucent overlay at this z-index, to dim it and block clicks on it.
export const kDisablingOverlayZIndex = 1001;

// The tool headers have to clear that overlay, and also the handful of controls inside the
// tool that deliberately opt out of it at one above it (the Help link in
// TalkingBookToolControls.tsx and the Show Playback Order switch itself in
// talkingBookAdvancedSection.tsx, both at kDisablingOverlayZIndex + 1). The offset lands on
// 1005, which is what the pre-6.4 "#toolbox h3" rule used, so this stays a like-for-like
// restoration and only the relationship between the numbers is new.
export const kToolboxHeaderZIndex = kDisablingOverlayZIndex + 4;
