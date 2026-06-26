import { css } from "@emotion/react";
import { ArrowDropDown } from "@mui/icons-material";
import CheckIcon from "@mui/icons-material/Check";
import Menu from "@mui/material/Menu";
import { useCallback, useEffect, useState } from "react";
import {
    kBloomBlue,
    kBloomPurple,
    kTextOnPurple,
} from "../../bloomMaterialUITheme";
import { BloomTooltip } from "../../react_components/BloomToolTip";
import BloomButton from "../../react_components/bloomButton";
import { get, post, postJson } from "../../utils/bloomApi";
import { callOnBlur } from "../../utils/menuCloseOnBlur";
import { useL10n } from "../../react_components/l10nHooks";
import { LocalizableMenuItem } from "../../react_components/localizableMenuItem";

interface ITopBarMenuItem {
    id: string;
    label: string;
    enabled: boolean;
    checked?: boolean;
}

const metricLayoutOrder = [
    "A6Portrait",
    "A6Landscape",
    "Cm13Landscape",
    "A5Portrait",
    "A5Landscape",
    "B5Portrait",
    "A4Portrait",
    "A4Landscape",
    "A3Portrait",
    "A3Landscape",
];

const imperialLayoutOrder = [
    "QuarterLetterPortrait",
    "QuarterLetterLandscape",
    "HalfLetterPortrait",
    "HalfLetterLandscape",
    "HalfFolioPortrait",
    "Size6x9Portrait",
    "Size6x9Landscape",
    "USComicPortrait",
    "LetterPortrait",
    "LetterLandscape",
    "LegalPortrait",
    "LegalLandscape",
];

const ebookLayoutOrder = [
    "Device16x9Portrait",
    "Device16x9Landscape",
    "Ebook2x3Portrait",
    "Ebook7x5Landscape",
];

const emphasizedLayoutIds = new Set([
    "A5Portrait",
    "Device16x9Portrait",
    "Device16x9Landscape",
]);

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

function isEbookLayout(item: ITopBarMenuItem): boolean {
    return (
        item.id.startsWith("Device16x9") ||
        item.id.startsWith("Ebook2x3") ||
        item.id.startsWith("Ebook7x5")
    );
}

function isMetricPaperLayout(item: ITopBarMenuItem): boolean {
    return /^(A\d|B\d|Cm\d)/.test(item.id);
}

function isUsPaperLayout(item: ITopBarMenuItem): boolean {
    return /^(Letter|Legal|HalfLetter|HalfFolio|QuarterLetter|USComic|Size6x9)/.test(
        item.id,
    );
}

function orderLayoutItems(
    items: ITopBarMenuItem[],
    orderedIds: string[],
): ITopBarMenuItem[] {
    const itemsById = new Map(items.map((item) => [item.id, item]));
    const orderedItems = orderedIds
        .map((id) => itemsById.get(id))
        .filter((item): item is ITopBarMenuItem => item !== undefined);
    const remainingItems = items.filter(
        (item) => !orderedIds.includes(item.id),
    );
    return [...orderedItems, ...remainingItems];
}

function renderLayoutChoiceItem(
    item: ITopBarMenuItem,
    onMenuItemClick: (item: ITopBarMenuItem) => void,
    buttonId: string,
) {
    return (
        <LocalizableMenuItem
            key={`${buttonId}-${item.id}-${item.label}`}
            english={item.label}
            l10nId={null}
            onClick={() => onMenuItemClick(item)}
            disabled={!item.enabled}
            icon={
                item.checked ? (
                    <CheckIcon
                        css={css`
                            color: ${kBloomBlue};
                            font-size: 22px;
                            font-weight: 700;
                        `}
                    />
                ) : undefined
            }
            hasLeadingIconSpace={true}
            labelCss={css`
                white-space: nowrap;
                font-weight: ${emphasizedLayoutIds.has(item.id)
                    ? 700
                    : 400} !important;
            `}
        />
    );
}

function renderLayoutChoiceGroup(
    heading: string,
    items: ITopBarMenuItem[],
    onMenuItemClick: (item: ITopBarMenuItem) => void,
    buttonId: string,
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
                padding-bottom: 4px;
            `}
        >
            <div
                css={css`
                    font-size: 12px;
                    font-weight: 700;
                    padding: 8px 16px 4px;
                `}
            >
                {heading}
            </div>
            {items.map((item) =>
                renderLayoutChoiceItem(item, onMenuItemClick, buttonId),
            )}
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
    const metricHeading = useL10n(
        "Metric Paper",
        "EditTab.LayoutChoicesMenu.Metric",
        "Heading for the metric paper-size group in the page size and orientation chooser dropdown on the edit tab.",
    );
    const usPaperHeading = useL10n(
        "Imperial Paper",
        "EditTab.LayoutChoicesMenu.USPaper",
        "Heading for the US paper-size group in the page size and orientation chooser dropdown on the edit tab. This includes sizes like Letter, Legal, Quarter Letter, US Comic, and 6x9.",
    );

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

    const { anchorEl, onClose, onOpen } = useLayoutChoicesMenuBehavior({
        buttonId: "layoutChoicesDropdownButton",
        menuItems: layoutChoiceMenuItems,
        loadMenuItems: loadLayoutChoiceData,
    });

    const onMenuItemClick = (item: ITopBarMenuItem) => {
        if (!item.enabled) {
            return;
        }
        setFallbackLocalizedText(item.label);
        postJson("editView/topBar/layoutChoiceChange", {
            layoutChoiceId: item.id,
        });
        post("editView/updateTopBarDropdownDisplay");
        onClose();
    };

    const renderMenuItems = (
        menuItems: ITopBarMenuItem[],
        onClick: (item: ITopBarMenuItem) => void,
        _showChecks: boolean,
        buttonId: string,
    ) => {
        if (menuItems.some((item) => item.id === "" && !item.enabled)) {
            return menuItems.map((item) =>
                renderLayoutChoiceItem(item, onClick, buttonId),
            );
        }

        const ebookItems = menuItems.filter(isEbookLayout);
        const paperItems = menuItems.filter((item) => !isEbookLayout(item));
        const metricItems = orderLayoutItems(
            paperItems.filter(isMetricPaperLayout),
            metricLayoutOrder,
        );
        const usPaperItems = orderLayoutItems(
            paperItems.filter(isUsPaperLayout),
            imperialLayoutOrder,
        );
        const orderedEbookItems = orderLayoutItems(
            ebookItems,
            ebookLayoutOrder,
        );

        return (
            <div
                css={css`
                    display: grid;
                    grid-template-columns: minmax(220px, 1fr) minmax(200px, 1fr);
                    column-gap: 8px;
                    align-items: start;
                `}
            >
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                        justify-content: flex-start;
                        gap: 20px;
                        padding-bottom: 8px;
                    `}
                >
                    {renderLayoutChoiceGroup(
                        metricHeading,
                        metricItems,
                        onClick,
                        buttonId,
                    )}
                    {renderLayoutChoiceGroup(
                        ebooksHeading,
                        orderedEbookItems,
                        onClick,
                        buttonId,
                    )}
                </div>
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                        padding-bottom: 8px;
                    `}
                >
                    <div
                        css={css`
                            font-size: 12px;
                            font-weight: 700;
                            padding: 10px 16px 6px;
                        `}
                    >
                        {usPaperHeading}
                    </div>
                    {usPaperItems.map((item) =>
                        renderLayoutChoiceItem(item, onClick, buttonId),
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
                                min-width: 500px;
                                max-width: 560px;
                            `,
                        },
                    }}
                >
                    {renderMenuItems(
                        layoutChoiceMenuItems,
                        onMenuItemClick,
                        false,
                        "layoutChoicesDropdownButton",
                    )}
                </Menu>
            </>
        </BloomTooltip>
    );
};
