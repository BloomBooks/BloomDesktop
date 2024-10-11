import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useState, useEffect, Fragment, useRef } from "react";
import * as ReactDOM from "react-dom";
import { kBloomBlue, lightTheme } from "../../bloomMaterialUITheme";
import { SvgIconProps } from "@mui/material";
import { default as CopyrightIcon } from "@mui/icons-material/Copyright";
import { default as SearchIcon } from "@mui/icons-material/Search";
import { default as MenuIcon } from "@mui/icons-material/MoreHorizSharp";
import { default as CopyIcon } from "@mui/icons-material/ContentCopy";
import { default as CheckIcon } from "@mui/icons-material/Check";
import { default as VolumeUpIcon } from "@mui/icons-material/VolumeUp";
import { default as PasteIcon } from "@mui/icons-material/ContentPaste";
import { default as CircleIcon } from "@mui/icons-material/Circle";
import { default as DeleteIcon } from "@mui/icons-material/DeleteOutline";
import { default as ArrowUpwardIcon } from "@mui/icons-material/ArrowUpward";
import { default as ArrowDownwardIcon } from "@mui/icons-material/ArrowDownward";
import { showCopyrightAndLicenseDialog } from "../editViewFrame";
import { doImageCommand, getImageUrlFromImageContainer } from "./bloomImages";
import {
    doVideoCommand,
    findNextVideoContainer,
    findPreviousVideoContainer
} from "./bloomVideo";
import {
    copyAndPlaySoundAsync,
    makeDuplicateOfDragBubble,
    makeTargetForBubble,
    playSound,
    showDialogToChooseSoundFileAsync
} from "../toolbox/dragActivity/dragActivityTool";
import { ThemeProvider } from "@mui/material/styles";
import {
    divider,
    ILocalizableMenuItemProps,
    LocalizableMenuItem,
    LocalizableNestedMenuItem
} from "../../react_components/localizableMenuItem";
import Menu from "@mui/material/Menu";
import { Divider } from "@mui/material";
import { DuplicateIcon } from "./DuplicateIcon";
import { BubbleManager, theOneBubbleManager } from "./bubbleManager";
import { copySelection, GetEditor, pasteClipboard } from "./bloomEditing";
import { BloomTooltip } from "../../react_components/BloomToolTip";
import { useL10n } from "../../react_components/l10nHooks";
import { CogIcon } from "./CogIcon";

interface IMenuItemWithSubmenu extends ILocalizableMenuItemProps {
    subMenu?: ILocalizableMenuItemProps[];
}

// This is the controls bar that appears beneath an overlay when it is selected. It contains buttons
// for the most common operations that apply to the overlay in its current state, and a menu for less common
// operations.

const OverlayContextControls: React.FunctionComponent<{
    overlay: HTMLElement;
    // These props support reusing the context controls menu for a right-click on the overlay.
    // The first two make the open state of the menu a controlled property. Basically the
    // parent stores the state and passes it in, but to get the normal behavior of
    // clicking on the "..." menu and closing the menu, this component can request that
    // it be changed. The third is the position of the menu, which is used when the menu
    // is opened by a right-click, to place it near the click.
    menuOpen: boolean;
    setMenuOpen: (open: boolean) => void;
    menuAnchorPosition?: { left: number; top: number };
}> = props => {
    const imgContainer = props.overlay.getElementsByClassName(
        "bloom-imageContainer"
    )[0];
    const hasImage = !!imgContainer;
    const img = imgContainer?.getElementsByTagName("img")[0];
    //const hasLicenseProblem = hasImage && !img.getAttribute("data-copyright");
    const videoContainer = props.overlay.getElementsByClassName(
        "bloom-videoContainer"
    )[0];
    const hasVideo = !!videoContainer;
    const video = videoContainer?.getElementsByTagName("video")[0];
    const videoSource = video?.getElementsByTagName("source")[0];
    const videoAlreadyChosen = !!videoSource?.getAttribute("src");
    const isPlaceHolder =
        hasImage && img.getAttribute("src")?.startsWith("placeHolder.png");
    const setMenuOpen = (open: boolean) => {
        // Even though we've done our best to tell the MUI menu NOT to steal focus, it seems it still does...
        // or some other code somewhere is doing it when we choose a menu item. So we tell the bubble manager
        // to ignore focus changes while the menu is open.
        BubbleManager.ignoreFocusChanges = open;
        props.setMenuOpen(open);
    };

    const menuEl = useRef<HTMLElement | null>();

    // Menu item names for 'none' and "Choose...", options.
    const noneLabel = useL10n("None", "EditTab.Toolbox.DragActivity.None", "");

    const currentBubbleTargetId = props.overlay?.getAttribute("data-bubble-id");
    const [currentBubbleTarget, setCurrentBubbleTarget] = useState<
        HTMLElement | undefined
    >();
    useEffect(() => {
        if (!currentBubbleTargetId) {
            setCurrentBubbleTarget(undefined);
            return;
        }
        const page = props.overlay.closest(".bloom-page") as HTMLElement;
        setCurrentBubbleTarget(
            page?.querySelector(
                `[data-target-of="${currentBubbleTargetId}"]`
            ) as HTMLElement
        );
        // We need to re-evaluate when changing pages, it's possible the initially selected item
        // on a new page has the same currentBubbleTargetId.
    }, [currentBubbleTargetId]);

    // Currently we only allow associating an extra audio with images (and gifs), which have
    // no other audio (except possibly image descriptions?). If we get an actual user request
    // it may be clearer how attaching one to a text or video would work, given that they
    // can already have narration or an audio channel.
    const canChooseAudioForElement = hasImage;

    const [imageSound, setImageSound] = useState("none");
    useEffect(() => {
        setImageSound(props.overlay.getAttribute("data-sound") ?? "none");
    }, [props.overlay]);

    // These commands apply to all overlays.
    const menuOptions: IMenuItemWithSubmenu[] = [
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.Duplicate",
            english: "Duplicate",
            onClick: theOneBubbleManager?.duplicateBubble,
            icon: <DuplicateIcon css={getMenuIconCss()} />
        },
        {
            l10nId: "Common.Delete",
            english: "Delete",
            onClick: theOneBubbleManager?.deleteBubble,
            icon: <DeleteIcon css={getMenuIconCss()} />
        }
    ];
    if (!hasImage && !hasVideo) {
        menuOptions.splice(0, 0, {
            l10nId: "EditTab.Toolbox.ComicTool.Options.AddChildBubble",
            english: "Add Child Bubble",
            onClick: theOneBubbleManager?.addChildBubble
        });
    }
    if (currentBubbleTargetId) {
        addMenuItemsForDraggable(
            menuOptions,
            props.overlay,
            currentBubbleTargetId,
            currentBubbleTarget,
            setCurrentBubbleTarget
        );
    }
    if (canChooseAudioForElement) {
        addAudioMenuItems(
            menuOptions,
            props.overlay,
            imageSound,
            noneLabel,
            setImageSound,
            setMenuOpen
        );
    }
    if (hasImage) {
        addImageMenuOptions(menuOptions, props.overlay, img);
    }
    if (hasVideo) {
        addVideoMenuItems(menuOptions, videoContainer);
    }
    const handleMenuButtonMouseDown = (e: React.MouseEvent) => {
        // This prevents focus leaving the text box.
        e.preventDefault();
        e.stopPropagation();
    };
    const handleMenuButtonMouseUp = (e: React.MouseEvent) => {
        // This prevents focus leaving the text box.
        e.preventDefault();
        e.stopPropagation();
        setMenuOpen(true); // Review: better on mouse down? But then the mouse up may be missed, if the menu is on top...
    };
    const editable = props.overlay.getElementsByClassName(
        "bloom-editable bloom-visibility-code-on"
    )[0] as HTMLElement;
    const langName = editable?.getAttribute("data-languagetipcontent");
    // and these for text boxes
    if (editable) {
        addTextMenuItems(menuOptions, editable, props.overlay);
    }

    return (
        <ThemeProvider theme={lightTheme}>
            <div
                css={css`
                    background-color: white;
                    border-radius: 3.785px;
                    border: 0.757px solid rgba(255, 255, 255, 0.2);
                    //opacity: 0.2;
                    box-shadow: 0px 0px 4px 0px rgba(0, 0, 0, 0.25);
                    border-radius: 4px;
                    display: flex;
                    flex-direction: column;
                    padding: 0px 10px 0px;
                    margin: 0 auto 0 auto;
                    width: fit-content;
                    // needed because it's a child of #overlay-context-controls which has pointer-events:none
                    pointer-events: all;
                `}
            >
                <div
                    css={css`
                        display: flex;
                        align-items: center;
                        // Not really sure what's going on here, since none of the buttons contain text
                        // But somehow they have a tendency to be several pixels higher than the contained
                        // icons, and this seems to be related to line-height. I don't want to set it
                        // to zero, in case (in some language) the tooltips wrap. But this seems to be small enough
                        // to prevent the problem.
                        line-height: 0.8em;
                        button {
                            line-height: 0.7em;
                        }
                    `}
                >
                    {hasImage && (
                        <Fragment>
                            {// Choose image is only a LIKELY choice if we don't yet have one.
                            isPlaceHolder && (
                                <ButtonWithTooltip
                                    tipL10nKey="EditTab.Image.ChooseImage"
                                    icon={SearchIcon}
                                    onClick={_ => {
                                        if (!props.overlay) return;
                                        const imgContainer = props.overlay.getElementsByClassName(
                                            "bloom-imageContainer"
                                        )[0] as HTMLElement;
                                        if (!imgContainer) return;
                                        doImageCommand(
                                            imgContainer.getElementsByTagName(
                                                "img"
                                            )[0] as HTMLImageElement,
                                            "change"
                                        );
                                    }}
                                />
                            )}
                            {isPlaceHolder && (
                                <ButtonWithTooltip
                                    tipL10nKey="EditTab.Image.PasteImage"
                                    icon={PasteIcon}
                                    relativeSize={0.9}
                                    onClick={_ => {
                                        if (!props.overlay) return;
                                        const imgContainer = props.overlay.getElementsByClassName(
                                            "bloom-imageContainer"
                                        )[0] as HTMLElement;
                                        if (!imgContainer) return;
                                        doImageCommand(
                                            imgContainer.getElementsByTagName(
                                                "img"
                                            )[0] as HTMLImageElement,
                                            "paste"
                                        );
                                    }}
                                ></ButtonWithTooltip>
                            )}
                        </Fragment>
                    )}
                    {editable && (
                        <ButtonWithTooltip
                            tipL10nKey="EditTab.Toolbox.ComicTool.Options.Format"
                            icon={CogIcon}
                            relativeSize={0.8}
                            onClick={() => {
                                if (!props.overlay) return;
                                GetEditor().runFormatDialog(editable);
                            }}
                        />
                    )}
                    {hasVideo && !videoAlreadyChosen && (
                        <Fragment>
                            <ButtonWithTooltip
                                tipL10nKey="EditTab.Toolbox.ComicTool.Options.ChooseVideo"
                                icon={SearchIcon}
                                onClick={() =>
                                    doVideoCommand(videoContainer, "choose")
                                }
                            />
                            <ButtonWithTooltip
                                tipL10nKey="EditTab.Toolbox.ComicTool.Options.RecordYourself"
                                icon={CircleIcon}
                                relativeSize={0.8}
                                onClick={() =>
                                    doVideoCommand(videoContainer, "record")
                                }
                            />
                        </Fragment>
                    )}
                    {(!(hasImage && isPlaceHolder) &&
                        !editable &&
                        !(hasVideo && !videoAlreadyChosen)) || (
                        // Add a spacer if there is any button before these
                        <div
                            css={css`
                                width: ${buttonWidth};
                            `}
                        />
                    )}
                    {!hasVideo && (
                        <ButtonWithTooltip
                            tipL10nKey="EditTab.Toolbox.ComicTool.Options.Duplicate"
                            icon={DuplicateIcon}
                            relativeSize={0.9}
                            onClick={() => {
                                if (!props.overlay) return;
                                makeDuplicateOfDragBubble();
                            }}
                        />
                    )}
                    <ButtonWithTooltip
                        tipL10nKey="Common.Delete"
                        icon={DeleteIcon}
                        onClick={() => {
                            if (!props.overlay) return;
                            theOneBubbleManager?.deleteBubble();
                        }}
                    />
                    <button
                        ref={ref => (menuEl.current = ref)}
                        css={getIconCss()}
                        // It would be more natural to handle a click. But clicks are a combination of
                        // mouse down and mouse up, and those have side effects, especially change of focus,
                        // that we need to prevent. So we handle them ourselves.
                        onMouseDown={handleMenuButtonMouseDown}
                        onMouseUp={handleMenuButtonMouseUp}
                    >
                        <MenuIcon color="primary" />
                    </button>
                    <Menu
                        css={css`
                            ul {
                                max-width: 260px;
                                li {
                                    display: flex;
                                    align-items: flex-start;
                                    p {
                                        white-space: initial;
                                    }
                                    &.MuiDivider-root {
                                        margin-bottom: 12px;
                                    }
                                }
                            }
                        `}
                        open={props.menuOpen}
                        // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
                        anchorEl={menuEl.current!}
                        anchorReference={
                            props.menuAnchorPosition
                                ? "anchorPosition"
                                : "anchorEl"
                        }
                        anchorPosition={props.menuAnchorPosition}
                        onClose={() => setMenuOpen(false)}
                        disableAutoFocus={true}
                        disableEnforceFocus={true}
                    >
                        {menuOptions.map((option, index) => {
                            if (option.l10nId === "-") {
                                return (
                                    <Divider
                                        key={index}
                                        variant="middle"
                                        component="li"
                                    />
                                );
                            }
                            if (option.subMenu) {
                                return (
                                    <LocalizableNestedMenuItem
                                        english={option.english}
                                        l10nId={option.l10nId}
                                        icon={option.icon}
                                        truncateMainLabel={true}
                                        subLabelL10nId={option.subLabelL10nId}
                                        disabled={option.disabled}
                                    >
                                        {option.subMenu.map(
                                            (subOption, subIndex) => (
                                                <LocalizableMenuItem
                                                    key={subIndex}
                                                    l10nId={subOption.l10nId}
                                                    english={subOption.english}
                                                    onClick={subOption.onClick}
                                                    icon={subOption.icon}
                                                    disabled={
                                                        subOption.disabled
                                                    }
                                                />
                                            )
                                        )}
                                    </LocalizableNestedMenuItem>
                                );
                            }
                            return (
                                <LocalizableMenuItem
                                    key={index}
                                    l10nId={option.l10nId}
                                    english={option.english}
                                    onClick={e => {
                                        setMenuOpen(false);
                                        option.onClick(e);
                                    }}
                                    disabled={option.disabled}
                                    icon={option.icon}
                                    variant="body1"
                                    subLabelL10nId={option.subLabelL10nId}
                                />
                            );
                        })}
                    </Menu>
                </div>
                {langName && (
                    <div
                        css={css`
                            color: ${kBloomBlue};
                            font-size: 10px;
                            margin-top: -4px; // pull it up tighter to the buttons
                            margin-left: 2px; // align with the first icon; the button has a 2px border
                        `}
                    >
                        {langName}
                    </div>
                )}
            </div>
        </ThemeProvider>
    );
};

const buttonWidth = "22px";

const ButtonWithTooltip: React.FunctionComponent<{
    icon: React.FunctionComponent<SvgIconProps>;
    tipL10nKey: string;
    onClick: React.MouseEventHandler;
    relativeSize?: number;
}> = props => {
    return (
        <BloomTooltip
            placement="top"
            tip={{
                l10nKey: props.tipL10nKey
            }}
        >
            <button
                onClick={props.onClick}
                css={getIconCss(props.relativeSize)}
            >
                <props.icon color="primary" />
            </button>
        </BloomTooltip>
    );
};

// This is used to render the OverlayContextControls as the root component of a div.
export function renderOverlayContextControls(
    overlay: HTMLElement,
    menuOpen: boolean,
    menuAnchorPosition?: { left: number; top: number }
) {
    const root = document.getElementById("overlay-context-controls");
    if (!root) {
        // not created yet, try later
        setTimeout(
            () =>
                renderOverlayContextControls(
                    overlay,
                    menuOpen,
                    menuAnchorPosition
                ),
            200
        );
        return;
    }
    ReactDOM.render(
        <OverlayContextControls
            overlay={overlay}
            menuOpen={menuOpen}
            setMenuOpen={(open: boolean) => {
                // turns out we don't need to store it anywhere. When it requests a change, we just
                // re-render it that way.
                renderOverlayContextControls(overlay, open);
            }}
            menuAnchorPosition={menuAnchorPosition}
        />,
        root
    );
}

function getIconCss(relativeSize?: number) {
    const defaultFontSize = 1.3;
    const fontSize = defaultFontSize * (relativeSize ?? 1);
    return css`
        border-color: transparent;
        background-color: transparent;
        vertical-align: middle;
        width: ${buttonWidth};
        svg {
            font-size: ${fontSize}rem;
        }
    `;
}

function getMenuIconCss(relativeSize?: number) {
    const defaultFontSize = 1.3;
    const fontSize = defaultFontSize * (relativeSize ?? 1);
    return css`
        color: black;
        font-size: ${fontSize}rem;
    `;
}

function addTextMenuItems(
    menuOptions: IMenuItemWithSubmenu[],
    editable: HTMLElement,
    overlay: HTMLElement
) {
    const autoHeight = !overlay.classList.contains("bloom-noAutoHeight");
    const toggleAutoHeight = () => {
        overlay.classList.toggle("bloom-noAutoHeight");
        theOneBubbleManager.updateAutoHeight();
        // In most contexts, we would need to do something now to make the control render, so we get
        // an updated value for autoHeight. But the menu is going to be hidden, and showing it again
        // will involve a re-render, and we don't care until then.
    };
    menuOptions.unshift(
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.Format",
            english: "Format",
            onClick: () => GetEditor().runFormatDialog(editable),
            icon: <CogIcon css={getMenuIconCss()} />
        },
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.CopyText",
            english: "Copy Text",
            onClick: () => copySelection(),
            icon: <CopyIcon css={getMenuIconCss()} />
        },
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.PasteText",
            english: "Paste Text",
            // We don't actually know there's no image on the clipboard, but it's not relevant for a text box.
            onClick: () => pasteClipboard(false),
            icon: <PasteIcon css={getMenuIconCss()} />
        },
        divider,
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.AutoHeight",
            english: "Auto Height",
            // We don't actually know there's no image on the clipboard, but it's not relevant for a text box.
            onClick: () => toggleAutoHeight(),
            icon: autoHeight && <CheckIcon css={getMenuIconCss()} />
        },
        divider
    );
}

function addVideoMenuItems(
    menuOptions: IMenuItemWithSubmenu[],
    videoContainer: Element
) {
    menuOptions.unshift(
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.ChooseVideo",
            english: "Choose Video from your Computer...",
            onClick: () => doVideoCommand(videoContainer, "choose"),
            icon: <SearchIcon css={getMenuIconCss()} />
        },
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.RecordYourself",
            english: "Record yourself...",
            onClick: () => doVideoCommand(videoContainer, "record"),
            icon: <CircleIcon css={getMenuIconCss(0.85)} />
        },
        divider,
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.PlayEarlier",
            english: "Play Earlier",
            onClick: () => {
                doVideoCommand(videoContainer, "playEarlier");
            },
            icon: <ArrowUpwardIcon css={getMenuIconCss()} />,
            disabled: !findPreviousVideoContainer(videoContainer)
        },
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.PlayLater",
            english: "Play Later",
            onClick: () => {
                doVideoCommand(videoContainer, "playLater");
            },
            icon: <ArrowDownwardIcon css={getMenuIconCss()} />,
            disabled: !findNextVideoContainer(videoContainer)
        },
        divider
    );
}

function addImageMenuOptions(
    menuOptions: IMenuItemWithSubmenu[],
    overlay: HTMLElement,
    img: HTMLElement
) {
    const runMetadataDialog = () => {
        if (!overlay) return;
        const imgContainer = overlay.getElementsByClassName(
            "bloom-imageContainer"
        )[0] as HTMLElement;
        if (!imgContainer) return;
        showCopyrightAndLicenseDialog(
            getImageUrlFromImageContainer(imgContainer)
        );
    };
    menuOptions.unshift(
        {
            l10nId: "EditTab.Image.ChooseImage",
            english: "Choose image from your computer...",
            onClick: () => doImageCommand(img, "change"),
            icon: <SearchIcon css={getMenuIconCss()} />
        },
        {
            l10nId: "EditTab.Image.PasteImage",
            english: "Paste image",
            onClick: () => doImageCommand(img, "paste"),
            icon: <PasteIcon css={getMenuIconCss()} />
        },
        {
            l10nId: "EditTab.Image.CopyImage",
            english: "Copy image",
            onClick: () => doImageCommand(img, "copy"),
            icon: <CopyIcon css={getMenuIconCss()} />
        },
        {
            l10nId: "EditTab.Image.EditMetadataOverlay",
            english: "Set Image Information...",
            subLabelL10nId: "EditTab.Image.EditMetadataOverlayMore",
            onClick: runMetadataDialog,
            icon: <CopyrightIcon css={getMenuIconCss()} />
        },
        divider
    );
}

function addMenuItemsForDraggable(
    menuOptions: IMenuItemWithSubmenu[],
    overlay: HTMLElement,
    currentBubbleTargetId: string,
    currentBubbleTarget: HTMLElement | undefined,
    setCurrentBubbleTarget: (target: HTMLElement | undefined) => void
) {
    const toggleIsPartOfRightAnswer = () => {
        if (!currentBubbleTargetId) {
            return;
        }
        if (currentBubbleTarget) {
            currentBubbleTarget.ownerDocument
                .getElementById("target-arrow")
                ?.remove();
            currentBubbleTarget.remove();
            setCurrentBubbleTarget(undefined);
        } else {
            setCurrentBubbleTarget(makeTargetForBubble(overlay));
        }
    };
    menuOptions.push(divider, {
        l10nId: "EditTab.Toolbox.DragActivity.PartOfRightAnswer",
        english: "Part of the right answer",
        subLabelL10nId: "EditTab.Toolbox.DragActivity.PartOfRightAnswerMore",
        onClick: toggleIsPartOfRightAnswer,
        icon: currentBubbleTarget ? (
            <CheckIcon css={getMenuIconCss()} />
        ) : (
            undefined
        )
    });
}

function addAudioMenuItems(
    menuOptions: IMenuItemWithSubmenu[],
    overlay: HTMLElement,
    imageSound: string,
    noneLabel: string,
    setImageSound: (sound: string) => void,
    setMenuOpen: (open: boolean) => void
) {
    // This is uncomfortably similar to the method by the same name in dragActivityTool.
    // And indeed that method has a case for handling an image sound, which is no longer
    // handled on the toolbox side. But both methods make use of component state in
    // ways that make sharing code difficult.
    const updateSoundShowingDialog = async () => {
        const newSoundId = await showDialogToChooseSoundFileAsync();
        if (!newSoundId) {
            return;
        }

        const page = overlay.closest(".bloom-page") as HTMLElement;
        const copyBuiltIn = false; // already copied, and not in our sounds folder
        overlay.setAttribute("data-sound", newSoundId);
        setImageSound(newSoundId);
        copyAndPlaySoundAsync(newSoundId, page, copyBuiltIn);
    };
    const imageSoundLabel = imageSound.replace(/.mp3$/, "");
    const mainLabel = imageSound === "none" ? noneLabel : imageSoundLabel;
    const subMenu: ILocalizableMenuItemProps[] = [
        {
            l10nId: "EditTab.Toolbox.DragActivity.None",
            english: "None",
            onClick: () => {
                overlay.removeAttribute("data-sound");
                setImageSound("none");
                setMenuOpen(false);
            }
        },
        {
            l10nId: "EditTab.Toolbox.DragActivity.ChooseSound",
            english: "Choose...",
            onClick: () => {
                setMenuOpen(false);
                updateSoundShowingDialog();
            }
        }
    ];
    menuOptions.push(divider, {
        l10nId: null,
        english: mainLabel,
        subLabelL10nId: "EditTab.Image.PlayWhenTouched",
        // eslint-disable-next-line @typescript-eslint/no-empty-function
        onClick: () => {},
        icon: <VolumeUpIcon css={getMenuIconCss()} />,
        subMenu
    });
    if (imageSound !== "none") {
        subMenu.splice(1, 0, {
            l10nId: null,
            english: imageSoundLabel,
            onClick: () => {
                playSound(
                    imageSound,
                    // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
                    overlay.closest(".bloom-page")!
                );
                setMenuOpen(false);
            },
            icon: <CheckIcon css={getMenuIconCss()} />
        });
    }
}
