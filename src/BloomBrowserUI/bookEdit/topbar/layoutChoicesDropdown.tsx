import { css } from "@emotion/react";
import { ArrowDropDown } from "@mui/icons-material";
import Menu from "@mui/material/Menu";
import { kBloomPurple, kTextOnPurple } from "../../bloomMaterialUITheme";
import { useCallback, useEffect, useRef, useState } from "react";
import { useMountEffect } from "../../utils/useMountEffect";
import { BloomTooltip } from "../../react_components/BloomToolTip";
import BloomButton from "../../react_components/bloomButton";
import { get, post, postJson } from "../../utils/bloomApi";
import { callOnBlur } from "../../utils/menuCloseOnBlur";
import { useL10n } from "../../react_components/l10nHooks";

// The page-size/orientation picker that lives in the edit-tab top bar. The
// trigger button shows the current size; clicking it opens a popover card that
// groups the available sizes into a metric column, an ebooks group, and an
// imperial column, each split by orientation, with the current size shown as a
// solid purple "pill". Only the "Ebooks" group carries a visible system
// heading; the metric and imperial groups are distinguished by position alone.
// Any backend-offered size that isn't in the curated lists below still appears,
// under an "Other" heading, so no available size is ever unselectable.

interface ITopBarMenuItem {
    id: string;
    label: string;
    enabled: boolean;
    checked?: boolean;
}

// Maps a backend layout id to the short label we show for it in the grid (the
// backend's full label is used only as a fallback, e.g. the "no other options"
// row). The arrays below also define which sizes appear, in what order, and in
// which group/orientation.
interface ILayoutDisplayItem {
    id: string;
    shortLabel: string;
}

const metricPortraitItems: ILayoutDisplayItem[] = [
    { id: "A3Portrait", shortLabel: "A3" },
    { id: "A4Portrait", shortLabel: "A4" },
    { id: "A5Portrait", shortLabel: "A5" },
    { id: "A6Portrait", shortLabel: "A6" },
    { id: "B5Portrait", shortLabel: "B5" },
];

const metricLandscapeItems: ILayoutDisplayItem[] = [
    { id: "A3Landscape", shortLabel: "A3" },
    { id: "A4Landscape", shortLabel: "A4" },
    { id: "A5Landscape", shortLabel: "A5" },
    { id: "A6Landscape", shortLabel: "A6" },
];

const metricSquareItems: ILayoutDisplayItem[] = [
    { id: "Cm13Landscape", shortLabel: "13cm" },
];

const imperialPortraitItems: ILayoutDisplayItem[] = [
    { id: "QuarterLetterPortrait", shortLabel: "Quarter Letter" },
    { id: "HalfLetterPortrait", shortLabel: "Half Letter" },
    { id: "HalfFolioPortrait", shortLabel: "Half Folio" },
    { id: "Size6x9Portrait", shortLabel: '6"x9"' },
    { id: "USComicPortrait", shortLabel: "US Comic" },
    { id: "LetterPortrait", shortLabel: "Letter" },
    { id: "LegalPortrait", shortLabel: "Legal" },
];

const imperialLandscapeItems: ILayoutDisplayItem[] = [
    { id: "QuarterLetterLandscape", shortLabel: "Quarter Letter" },
    { id: "HalfLetterLandscape", shortLabel: "Half Letter" },
    { id: "Size6x9Landscape", shortLabel: '9"x6"' },
    { id: "LetterLandscape", shortLabel: "Letter" },
    { id: "LegalLandscape", shortLabel: "Legal" },
];

const ebookPortraitItems: ILayoutDisplayItem[] = [
    { id: "Device16x9Portrait", shortLabel: "9x16" },
    { id: "Ebook2x3Portrait", shortLabel: "2x3" },
];

const ebookLandscapeItems: ILayoutDisplayItem[] = [
    { id: "Device16x9Landscape", shortLabel: "16x9" },
    { id: "Ebook7x5Landscape", shortLabel: "7x5" },
];

// Every layout id that one of the curated groups above knows how to place. Any
// backend-offered choice whose id is not in this set is rendered in the catch-all
// "Other" group (see getOtherDisplayItems) so it stays visible and selectable,
// matching the old dropdown which listed every backend choice.
const curatedLayoutIds = new Set(
    [
        metricPortraitItems,
        metricLandscapeItems,
        metricSquareItems,
        imperialPortraitItems,
        imperialLandscapeItems,
        ebookPortraitItems,
        ebookLandscapeItems,
    ].flatMap((group) => group.map((item) => item.id)),
);

// Sizes we want to draw the eye to (the common/recommended choices). These are
// shown bold in the menu when not selected.
const emphasizedLayoutIds = new Set([
    "A5Portrait",
    "Device16x9Portrait",
    "Device16x9Landscape",
]);

type OrientationShape = "portrait" | "landscape" | "square";

// A 14x14 rounded-rectangle orientation glyph (stroke follows the surrounding
// text color via currentColor) drawn per shape: an upright rectangle for
// portrait, a wide one for landscape, and a square.
const OrientationIcon: React.FunctionComponent<{ shape: OrientationShape }> = (
    props,
) => {
    // Keep each glyph centered within the 14x14 viewBox (so y = (14 - height)/2)
    // so it vertical-centers cleanly against the heading text beside it.
    const rect =
        props.shape === "portrait"
            ? { x: 4.5, y: 1, width: 7, height: 12 }
            : props.shape === "landscape"
              ? { x: 2, y: 3.5, width: 12, height: 7 }
              : { x: 3, y: 2, width: 10, height: 10 };
    return (
        <svg
            width={12}
            height={12}
            viewBox="0 0 14 14"
            fill="none"
            css={css`
                flex: 0 0 auto;
            `}
        >
            <rect
                x={rect.x}
                y={rect.y}
                width={rect.width}
                height={rect.height}
                rx="1.5"
                stroke="currentColor"
                strokeWidth={1.4}
                fill="none"
            />
        </svg>
    );
};

// Manages opening/closing of the popover: it anchors the menu to the trigger
// button and arranges to close it when the containing window loses focus.
function useLayoutChoicesMenuBehavior(props: {
    buttonId: string;
    menuItems: ITopBarMenuItem[];
    loadMenuItems: (onLoaded?: (itemCount: number) => void) => void;
}) {
    const [anchorEl, setAnchorEl] = useState<HTMLElement>();

    const openMenuAtAnchor = (anchorElement: HTMLElement) => {
        setAnchorEl(anchorElement);
        callOnBlur(() => setAnchorEl(undefined));
    };

    const onClose = () => {
        setAnchorEl(undefined);
    };

    const onOpen = () => {
        const anchorElement = document.getElementById(props.buttonId);
        if (!anchorElement) {
            return;
        }

        // If we already have items, open immediately and refresh in the
        // background; otherwise wait for the load and only open if there is
        // something to show.
        if (props.menuItems.length > 0) {
            openMenuAtAnchor(anchorElement);
            props.loadMenuItems();
            return;
        }

        props.loadMenuItems((itemCount) => {
            if (itemCount > 0) {
                openMenuAtAnchor(anchorElement);
            } else {
                setAnchorEl(undefined);
            }
        });
    };

    return {
        anchorEl,
        onClose,
        onOpen,
    };
}

// Convert the raw API response into menu items, marking the current choice as
// checked. If there is only one (or no) real choice, add a disabled
// "no other options" row so the menu is not empty.
function normalizeLayoutChoiceItems(
    choices: unknown,
    currentLayoutChoiceId: unknown,
    noOtherLayoutsText: string,
): ITopBarMenuItem[] {
    if (!Array.isArray(choices)) {
        return [];
    }

    const currentId = String(currentLayoutChoiceId ?? "");
    const normalizedChoices = choices.map((choice) => {
        const choiceInfo = choice as Record<string, unknown>;
        const id = String(choiceInfo.id ?? "");
        return {
            id,
            label: String(choiceInfo.label ?? ""),
            enabled: true,
            checked: id === currentId,
        };
    });

    if (normalizedChoices.length < 2) {
        normalizedChoices.push({
            id: "",
            label: noOtherLayoutsText,
            enabled: false,
            checked: false,
        });
    }

    return normalizedChoices;
}

// For a group's definition list, return the corresponding loaded menu items
// (with their short labels), skipping any sizes the current template does not
// offer.
function getDisplayItems(
    itemsById: Map<string, ITopBarMenuItem>,
    definitions: ILayoutDisplayItem[],
): Array<ITopBarMenuItem & { shortLabel: string }> {
    return definitions
        .map((definition) => {
            const item = itemsById.get(definition.id);
            return item
                ? {
                      ...item,
                      shortLabel: definition.shortLabel,
                  }
                : undefined;
        })
        .filter(
            (
                item,
            ): item is ITopBarMenuItem & {
                shortLabel: string;
            } => item !== undefined,
        );
}

// Return any loaded menu items the curated groups don't place (an id not in
// curatedLayoutIds), using the backend's full label as the display label. This
// is the safety net that keeps an unanticipated template/book size selectable.
function getOtherDisplayItems(
    menuItems: ITopBarMenuItem[],
): Array<ITopBarMenuItem & { shortLabel: string }> {
    return menuItems
        .filter((item) => item.id !== "" && !curatedLayoutIds.has(item.id))
        .map((item) => ({ ...item, shortLabel: item.label }));
}

// Render one clickable size. Every item — selected or not — occupies the exact
// same box as the selected purple pill (same padding, radius, and reserved bold
// width), so selecting an item only changes its background/color/weight and never
// reflows the menu. A few "recommended" sizes are shown bold even when unselected.
function renderLayoutChoiceItem(
    item: ITopBarMenuItem & { shortLabel: string },
    onMenuItemClick: (item: ITopBarMenuItem) => void,
    buttonId: string,
) {
    const bold = item.checked || emphasizedLayoutIds.has(item.id);
    return (
        <button
            key={`${buttonId}-${item.id}-${item.shortLabel}`}
            type="button"
            // This is a single-select list of sizes, so expose each option as a
            // radio-style menu item and announce which one is currently chosen.
            role="menuitemradio"
            aria-checked={!!item.checked}
            onClick={() => onMenuItemClick(item)}
            disabled={!item.enabled}
            css={css`
                justify-self: start;
                display: inline-flex;
                align-items: center;
                border: 0;
                cursor: ${item.enabled ? "pointer" : "default"};
                opacity: ${item.enabled ? 1 : 0.5};
                text-align: left;
                white-space: nowrap;
                font-family: inherit;
                font-size: 15px;
                padding: 4px 14px;
                border-radius: 999px;
                background-color: ${item.checked ? "#8a5a96" : "transparent"};
                color: ${item.checked ? "#fff" : "#222"};
                font-weight: ${bold ? 700 : 400};

                &:hover {
                    background-color: ${item.checked ? "#8a5a96" : "#f5f1f7"};
                }

                &:focus-visible {
                    outline: 2px solid #1d94a4;
                    outline-offset: 1px;
                }
            `}
        >
            {/* The hidden bold copy (::after) reserves the bold width so that
                becoming selected/bold does not change the item's width. */}
            <span
                data-text={item.shortLabel}
                css={css`
                    display: flex;
                    flex-direction: column;

                    &::after {
                        content: attr(data-text);
                        height: 0;
                        font-weight: 700;
                        overflow: hidden;
                        visibility: hidden;
                    }
                `}
            >
                {item.shortLabel}
            </span>
        </button>
    );
}

// Render an orientation block: a "PORTRAIT"/"LANDSCAPE"/"SQUARE" header preceded
// by its icon, followed by the grid of sizes for that orientation.
function renderLayoutChoiceSection(
    heading: string,
    items: Array<ITopBarMenuItem & { shortLabel: string }>,
    onMenuItemClick: (item: ITopBarMenuItem) => void,
    buttonId: string,
    shape: OrientationShape,
    variant: "metric" | "imperial",
    // When this is the first section of a group that has no system heading above
    // it, collapse the header's top margin so it sits flush with the body padding.
    collapseTopMargin: boolean,
) {
    if (items.length === 0) {
        return undefined;
    }

    return (
        <div
            key={heading}
            css={css`
                display: flex;
                flex-direction: column;
            `}
        >
            <div
                css={css`
                    display: flex;
                    align-items: center;
                    gap: 6px;
                    margin: ${collapseTopMargin ? 0 : 12}px 0 1px;
                    font-size: 10.5px;
                    font-weight: 700;
                    /* Tight line box so centering aligns the icon to the visible
                       (uppercase) caps rather than to a tall line box whose empty
                       descender space would pull the icon below the text. */
                    line-height: 1;
                    letter-spacing: 0.08em;
                    text-transform: uppercase;
                    color: #9a8aa0;
                `}
            >
                <OrientationIcon shape={shape} />
                {heading}
            </div>
            <div
                css={css`
                    display: grid;
                    grid-template-columns: ${variant === "metric"
                        ? "1fr 1fr"
                        : "1fr"};
                    column-gap: 18px;
                    /* Indent so a label lines up under the header text. Each item
                       carries 14px of internal (pill) left padding, so this is
                       19px (the desired text indent) minus that 14px. */
                    padding-left: 5px;
                `}
            >
                {items.map((item) =>
                    renderLayoutChoiceItem(item, onMenuItemClick, buttonId),
                )}
            </div>
        </div>
    );
}

export const LayoutChoicesDropdown: React.FunctionComponent<{
    localizedText: string;
}> = (props) => {
    const [layoutChoiceMenuItems, setLayoutChoiceMenuItems] = useState<
        ITopBarMenuItem[]
    >([]);
    const [fallbackLocalizedText, setFallbackLocalizedText] = useState("");
    // Holds the pending "apply the layout change" timer (see onMenuItemClick) so
    // we can restart it on a rapid re-click and cancel it if we unmount first.
    const applyChangeTimerRef = useRef<number | undefined>(undefined);
    const noOtherLayoutsText = useL10n(
        "There are no other options for this template.",
        "EditTab.NoOtherLayouts",
        "Show in the size/orientation chooser dropdown of the edit tab, if there was only a single choice",
    );
    const ebooksHeading = useL10n(
        "Ebooks",
        "EditTab.LayoutChoicesMenu.Ebooks",
        "Heading for the ebook-size column in the page size and orientation chooser dropdown on the edit tab.",
    );
    const portraitHeading = useL10n(
        "Portrait",
        "EditTab.LayoutChoicesMenu.Portrait",
        "Section heading for portrait page sizes in the page size and orientation chooser dropdown on the edit tab.",
    );
    const landscapeHeading = useL10n(
        "Landscape",
        "EditTab.LayoutChoicesMenu.Landscape",
        "Section heading for landscape page sizes in the page size and orientation chooser dropdown on the edit tab.",
    );
    const squareHeading = useL10n(
        "Square",
        "EditTab.LayoutChoicesMenu.Square",
        "Section heading for square page sizes in the page size and orientation chooser dropdown on the edit tab.",
    );
    const otherHeading = useL10n(
        "Other",
        "EditTab.LayoutChoicesMenu.Other",
        "Heading for the catch-all group in the page size and orientation chooser dropdown on the edit tab. It lists any sizes a template offers that don't fit the standard metric, ebook, or imperial groups.",
    );

    // Fetch the available layout choices (and which one is current) from the
    // backend. onLoaded is used by the open logic to decide whether to show the
    // menu.
    const loadLayoutChoiceData = useCallback(
        (onLoaded?: (itemCount: number) => void) => {
            get("editView/topBar/layoutChoiceData", (result) => {
                const items = normalizeLayoutChoiceItems(
                    result.data?.choices,
                    result.data?.currentLayoutChoiceId,
                    noOtherLayoutsText,
                );
                const currentChoice = items.find((item) => item.checked);
                if (currentChoice?.label) {
                    setFallbackLocalizedText(currentChoice.label);
                }
                onLoaded?.(items.length);
                setLayoutChoiceMenuItems(items);
            });
        },
        [noOtherLayoutsText],
    );

    useEffect(() => {
        loadLayoutChoiceData();
    }, [loadLayoutChoiceData]);

    // Cancel any pending layout-change timer if we unmount before it fires, so we
    // don't post a change / set state on an unmounted component.
    useMountEffect(() => {
        return () => {
            if (applyChangeTimerRef.current !== undefined) {
                window.clearTimeout(applyChangeTimerRef.current);
            }
        };
    });

    const { anchorEl, onClose, onOpen } = useLayoutChoicesMenuBehavior({
        buttonId: "layoutChoicesDropdownButton",
        menuItems: layoutChoiceMenuItems,
        loadMenuItems: loadLayoutChoiceData,
    });

    // Apply the user's size choice. We briefly leave the menu open showing the
    // new selection before actually changing the layout.
    const onMenuItemClick = (item: ITopBarMenuItem) => {
        if (!item.enabled) {
            return;
        }
        // Reflect the new selection immediately so the selected pill moves to the
        // clicked item and is visible briefly before the menu closes.
        setLayoutChoiceMenuItems((prev) =>
            prev.map((menuItem) => ({
                ...menuItem,
                checked: menuItem.id === item.id,
            })),
        );
        // Delay the actual layout change: posting it re-lays-out the page and
        // re-renders the top bar, which tears down the open menu. We want the
        // user to see the selected pill for a moment first, so we apply the
        // change (and close) only after a short delay. Restart the timer on each
        // click so a rapid re-click just updates the selection (last one wins)
        // rather than posting multiple changes.
        if (applyChangeTimerRef.current !== undefined) {
            window.clearTimeout(applyChangeTimerRef.current);
        }
        applyChangeTimerRef.current = window.setTimeout(() => {
            applyChangeTimerRef.current = undefined;
            setFallbackLocalizedText(item.label);
            postJson("editView/topBar/layoutChoiceChange", {
                layoutChoiceId: item.id,
            });
            post("editView/updateTopBarDropdownDisplay");
            onClose();
        }, 300);
    };

    // Build the popover body: the two-column grid of size groups. If the only
    // "item" is the disabled placeholder, render just that.
    const renderMenuItems = (
        menuItems: ITopBarMenuItem[],
        onClick: (item: ITopBarMenuItem) => void,
        buttonId: string,
    ) => {
        if (menuItems.some((item) => item.id === "" && !item.enabled)) {
            return (
                <div
                    css={css`
                        padding: 18px 22px 22px;
                    `}
                >
                    {menuItems.map((item) =>
                        renderLayoutChoiceItem(
                            { ...item, shortLabel: item.label },
                            onClick,
                            buttonId,
                        ),
                    )}
                </div>
            );
        }

        const itemsById = new Map(menuItems.map((item) => [item.id, item]));
        const metricPortraitDisplayItems = getDisplayItems(
            itemsById,
            metricPortraitItems,
        );
        const metricLandscapeDisplayItems = getDisplayItems(
            itemsById,
            metricLandscapeItems,
        );
        const metricSquareDisplayItems = getDisplayItems(
            itemsById,
            metricSquareItems,
        );
        const imperialPortraitDisplayItems = getDisplayItems(
            itemsById,
            imperialPortraitItems,
        );
        const imperialLandscapeDisplayItems = getDisplayItems(
            itemsById,
            imperialLandscapeItems,
        );
        const ebookPortraitDisplayItems = getDisplayItems(
            itemsById,
            ebookPortraitItems,
        );
        const ebookLandscapeDisplayItems = getDisplayItems(
            itemsById,
            ebookLandscapeItems,
        );
        const otherDisplayItems = getOtherDisplayItems(menuItems);

        // The group heading style (used for the "Ebooks" and "Other" groups; the
        // Metric and Imperial headings are intentionally omitted from the design).
        const systemHeadingCss = css`
            font-size: 13px;
            font-weight: 700;
            letter-spacing: 0.02em;
            color: #5f5f5f;
        `;

        return (
            <div
                css={css`
                    display: grid;
                    /* size each column to its own content so the card hugs the
                       content and the white space is even on all sides */
                    grid-template-columns: auto auto;
                    gap: 26px;
                    align-items: start;
                    padding: 22px;
                `}
            >
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                    `}
                >
                    {/* Metric group (heading intentionally omitted) */}
                    <div>
                        {renderLayoutChoiceSection(
                            portraitHeading,
                            metricPortraitDisplayItems,
                            onClick,
                            buttonId,
                            "portrait",
                            "metric",
                            true,
                        )}
                        {renderLayoutChoiceSection(
                            landscapeHeading,
                            metricLandscapeDisplayItems,
                            onClick,
                            buttonId,
                            "landscape",
                            "metric",
                            false,
                        )}
                        {renderLayoutChoiceSection(
                            squareHeading,
                            metricSquareDisplayItems,
                            onClick,
                            buttonId,
                            "square",
                            "metric",
                            false,
                        )}
                    </div>
                    {/* Ebooks group, set off from the metric group above it */}
                    <div
                        css={css`
                            margin-top: 22px;
                        `}
                    >
                        <div css={systemHeadingCss}>{ebooksHeading}</div>
                        {renderLayoutChoiceSection(
                            portraitHeading,
                            ebookPortraitDisplayItems,
                            onClick,
                            buttonId,
                            "portrait",
                            "metric",
                            false,
                        )}
                        {renderLayoutChoiceSection(
                            landscapeHeading,
                            ebookLandscapeDisplayItems,
                            onClick,
                            buttonId,
                            "landscape",
                            "metric",
                            false,
                        )}
                    </div>
                </div>
                {/* Imperial group (heading intentionally omitted) */}
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                    `}
                >
                    {renderLayoutChoiceSection(
                        portraitHeading,
                        imperialPortraitDisplayItems,
                        onClick,
                        buttonId,
                        "portrait",
                        "imperial",
                        true,
                    )}
                    {renderLayoutChoiceSection(
                        landscapeHeading,
                        imperialLandscapeDisplayItems,
                        onClick,
                        buttonId,
                        "landscape",
                        "imperial",
                        false,
                    )}
                    {/* Catch-all for sizes the curated groups don't place, so an
                        unanticipated template/book size is still selectable. */}
                    {otherDisplayItems.length > 0 && (
                        <div
                            css={css`
                                margin-top: 22px;
                            `}
                        >
                            <div css={systemHeadingCss}>{otherHeading}</div>
                            <div
                                css={css`
                                    display: grid;
                                    grid-template-columns: 1fr;
                                    padding-left: 5px;
                                `}
                            >
                                {otherDisplayItems.map((item) =>
                                    renderLayoutChoiceItem(
                                        item,
                                        onClick,
                                        buttonId,
                                    ),
                                )}
                            </div>
                        </div>
                    )}
                </div>
            </div>
        );
    };

    return (
        <BloomTooltip
            tip={{ l10nKey: "EditTab.PageSizeAndOrientation.Tooltip" }}
            slotProps={{
                tooltip: { sx: { maxWidth: "167px", "font-size": "11px" } },
            }}
        >
            <>
                <BloomButton
                    id="layoutChoicesDropdownButton"
                    onClick={onOpen}
                    enabled={true}
                    l10nKey="layoutChoicesDropdownButton"
                    alreadyLocalized={true}
                    iconBeforeText={<ArrowDropDown />}
                    size="small"
                    variant="text"
                    disableRipple
                    disableElevation
                    disableFocusRipple
                    disableTouchRipple
                    css={css`
                        font-size: 11px;
                        padding: 1px 5px 2px 5px;
                        text-transform: none;
                        width: fit-content;
                        min-width: unset;
                        background-color: ${kBloomPurple};
                        color: ${kTextOnPurple};
                        border: hidden;
                        flex-direction: row-reverse;

                        .MuiButton-startIcon {
                            margin-right: 0;
                            margin-left: 4px;
                        }
                    `}
                >
                    {props.localizedText || fallbackLocalizedText}
                </BloomButton>
                <Menu
                    open={Boolean(anchorEl)}
                    anchorEl={anchorEl}
                    onClose={onClose}
                    disablePortal={false}
                    keepMounted={false}
                    anchorOrigin={{
                        vertical: "bottom",
                        horizontal: "left",
                    }}
                    transformOrigin={{
                        vertical: "top",
                        horizontal: "left",
                    }}
                    slotProps={{
                        paper: {
                            css: css`
                                background-color: #fff;
                                border: 1px solid #e2e2e2;
                                border-radius: 6px;
                                box-shadow: 0 6px 22px rgba(40, 20, 55, 0.14);
                                overflow: hidden;
                            `,
                        },
                    }}
                >
                    {renderMenuItems(
                        layoutChoiceMenuItems,
                        onMenuItemClick,
                        "layoutChoicesDropdownButton",
                    )}
                </Menu>
            </>
        </BloomTooltip>
    );
};
