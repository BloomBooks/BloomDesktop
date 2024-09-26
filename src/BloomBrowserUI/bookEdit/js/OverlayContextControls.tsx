import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useState, useEffect, Fragment, useRef } from "react";
import * as ReactDOM from "react-dom";
import { kBloomBlue, lightTheme } from "../../bloomMaterialUITheme";
import { default as CopyrightIcon } from "@mui/icons-material/Copyright";
import { default as SearchIcon } from "@mui/icons-material/Search";
import { default as MenuIcon } from "@mui/icons-material/MoreHorizSharp";
import { default as CopyIcon } from "@mui/icons-material/ContentCopy";
import { default as CheckIcon } from "@mui/icons-material/Check";
import { default as VolumeUpIcon } from "@mui/icons-material/VolumeUp";
import { default as PasteIcon } from "@mui/icons-material/ContentPaste";
import { default as Circle } from "@mui/icons-material/Circle";
import { showCopyrightAndLicenseDialog } from "../editViewFrame";
import { doImageCommand, getImageUrlFromImageContainer } from "./bloomImages";
import { doVideoCommand } from "./bloomVideo";
import {
    copyAndPlaySoundAsync,
    makeDuplicateOfDragBubble,
    makeTargetForBubble,
    playSound,
    showDialogToChooseSoundFileAsync
} from "../toolbox/dragActivity/dragActivityTool";
import { ThemeProvider } from "@mui/material/styles";
import {
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
import { TrashIcon } from "../toolbox/overlay/TrashIcon";
import { useL10n } from "../../react_components/l10nHooks";

const controlFrameColor: string = kBloomBlue;

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
    // Some of the icons we use for buttons are Material UI ones. They need this CSS to look right.
    const materialIconCss = (svgsize?: number) => css`
        height: 30px;
        border-color: transparent;
        background-color: transparent;
        // These tweaks help make a neat row of aligned buttons the same size.
        top: -4px; // wants 3px if we remove align-items:start
        position: relative;
        svg {
            font-size: ${svgsize ?? 1.7}rem;
        }
    `;
    // Some of the icons we use for buttons are SVGs. They need this CSS to look right and similar to
    // the Material UI ones.
    const svgIconCss = css`
        height: 23px;
        position: relative;
        //top: 7px; // restore if we remove align-items:start
        top: -1px;
        border-color: transparent;
        background-color: transparent;
    `;

    const runMetadataDialog = () => {
        if (!props.overlay) return;
        const imgContainer = props.overlay.getElementsByClassName(
            "bloom-imageContainer"
        )[0] as HTMLElement;
        if (!imgContainer) return;
        showCopyrightAndLicenseDialog(
            getImageUrlFromImageContainer(imgContainer)
        );
    };

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
    const chooseLabel = useL10n(
        "Choose...",
        "EditTab.Toolbox.DragActivity.ChooseSound",
        ""
    );

    const menuIconColor = "black";
    const muiMenIconCss = css`
        color: ${menuIconColor};
    `;
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
            setCurrentBubbleTarget(makeTargetForBubble(props.overlay));
        }
    };

    // Currently we only allow associating an extra audio with images (and gifs), which have
    // no other audio (except possibly image descriptions?). If we get an actual user request
    // it may be clearer how attaching one to a text or video would work, given that they
    // can already have narration or an audio channel.
    const canChooseAudioForElement = hasImage;

    const [imageSound, setImageSound] = useState("none");
    useEffect(() => {
        setImageSound(props.overlay.getAttribute("data-sound") ?? "none");
    }, [props.overlay]);

    // This is uncomfortably similar to the method by the same name in dragActivityTool.
    // And indeed that method has a case for handling an image sound, which is no longer
    // handled on the toolbox side. But both methods make use of component state in
    // ways that make sharing code difficult.
    const updateSoundShowingDialog = async () => {
        const newSoundId = await showDialogToChooseSoundFileAsync();
        if (!newSoundId) {
            return;
        }

        const page = props.overlay.closest(".bloom-page") as HTMLElement;
        const copyBuiltIn = false; // already copied, and not in our sounds folder
        props.overlay.setAttribute("data-sound", newSoundId);
        setImageSound(newSoundId);
        copyAndPlaySoundAsync(newSoundId, page, copyBuiltIn);
    };

    // These commands apply to all overlays.
    const menuOptions: IMenuItemWithSubmenu[] = [
        {
            l10nId: "EditTab.Toolbox.ComicTool.Options.Duplicate",
            english: "Duplicate",
            onClick: theOneBubbleManager?.duplicateBubble,
            icon: (
                <DuplicateIcon
                    css={css`
                        width: 18px;
                    `}
                    color={menuIconColor}
                />
            )
        },
        {
            l10nId: "Common.Delete",
            english: "Delete",
            onClick: theOneBubbleManager?.deleteBubble,
            icon: (
                <TrashIcon
                    color="black"
                    css={css`
                        position: relative;
                        top: -5px;
                        left: -4px;
                    `}
                />
            )
        }
    ];
    if (!hasImage && !hasVideo) {
        menuOptions.splice(0, 0, {
            l10nId: "EditTab.Toolbox.ComicTool.Options.AddChildBubble",
            english: "Add Child Bubble",
            onClick: theOneBubbleManager?.addChildBubble
        });
    }
    if (currentBubbleTargetId || canChooseAudioForElement) {
        menuOptions.push({
            l10nId: "-",
            english: "",
            onClick: () => {}
        });
    }
    if (currentBubbleTargetId) {
        menuOptions.push({
            l10nId: "EditTab.Toolbox.DragActivity.PartOfRightAnswer",
            english: "Part of the right answer",
            subLabelL10nId:
                "EditTab.Toolbox.DragActivity.PartOfRightAnswerMore",
            onClick: toggleIsPartOfRightAnswer,
            icon: currentBubbleTarget ? (
                <CheckIcon css={muiMenIconCss} />
            ) : (
                undefined
            )
        });
    }
    if (canChooseAudioForElement) {
        const imageSoundLabel = imageSound.replace(/.mp3$/, "");
        const mainLabel = imageSound === "none" ? noneLabel : imageSoundLabel;
        const subMenu: ILocalizableMenuItemProps[] = [
            {
                l10nId: "EditTab.Toolbox.DragActivity.None",
                english: "None",
                onClick: () => {
                    props.overlay.removeAttribute("data-sound");
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
        menuOptions.push({
            l10nId: null,
            english: mainLabel,
            subLabelL10nId: "EditTab.Image.PlayWhenTouched",
            onClick: () => {},
            icon: <VolumeUpIcon css={muiMenIconCss} />,
            subMenu
        });
        if (imageSound !== "none") {
            subMenu.splice(1, 0, {
                l10nId: null,
                english: imageSoundLabel,
                onClick: () => {
                    // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
                    playSound(
                        imageSound,
                        props.overlay.closest(".bloom-page")!
                    );
                    setMenuOpen(false);
                },
                icon: <CheckIcon css={muiMenIconCss} />
            });
        }
    }

    // Add these for images
    if (hasImage) {
        menuOptions.unshift(
            {
                l10nId: "EditTab.Image.ChooseImage",
                english: "Choose image from your computer...",
                onClick: () => doImageCommand(img, "change"),
                icon: <SearchIcon css={muiMenIconCss} />
            },
            {
                l10nId: "EditTab.Image.PasteImage",
                english: "Paste image",
                onClick: () => doImageCommand(img, "paste"),
                icon: <PasteIcon css={muiMenIconCss} />
            },
            {
                l10nId: "EditTab.Image.CopyImage",
                english: "Copy image",
                onClick: () => doImageCommand(img, "copy"),
                icon: <CopyIcon css={muiMenIconCss} />
            },
            {
                l10nId: "EditTab.Image.EditMetadataOverlay",
                english: "Set Image Information...",
                subLabelL10nId: "EditTab.Image.EditMetadataOverlayMore",
                onClick: runMetadataDialog,
                icon: <CopyrightIcon css={muiMenIconCss} />
            },
            {
                l10nId: "-",
                english: "",
                onClick: () => {}
            }
        );

        // menuOptions.push(
        //     {
        //         l10nId: "-",
        //         english: "",
        //         onClick: () => {}
        //     },

        //     {
        //         l10nId: "EditTab.Image.CutImage",
        //         english: "Cut Image",
        //         onClick: () => doImageCommand(img, "cut"),
        //         icon: <CutIcon css={muiMenIconCss} />
        //     },

        // );
    }

    // Add these for videos
    if (hasVideo) {
        menuOptions.unshift(
            {
                l10nId: "EditTab.Toolbox.ComicTool.Options.ChooseVideo",
                english: "Choose Video from your Computer...",
                onClick: () => doVideoCommand(videoContainer, "choose"),
                icon: <SearchIcon css={muiMenIconCss} />
            },
            {
                l10nId: "EditTab.Toolbox.ComicTool.Options.RecordYourself",
                english: "Record yourself...",
                onClick: () => doVideoCommand(videoContainer, "record"),
                icon: <Circle css={muiMenIconCss} viewBox="0 0 28 28" />
            },
            {
                l10nId: "-",
                english: "",
                onClick: () => {
                    /*do nothing*/
                }
            }
        );
    }
    const autoHeight = !props.overlay.classList.contains("bloom-noAutoHeight");
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
    const toggleAutoHeight = () => {
        props.overlay.classList.toggle("bloom-noAutoHeight");
        theOneBubbleManager.updateAutoHeight();
        // In most contexts, we would need to do something now to make the control render, so we get
        // an updated value for autoHeight. But the menu is going to be hidden, and showing it again
        // will involve a re-render, and we don't care until then.
    };
    const editable = props.overlay.getElementsByClassName(
        "bloom-editable bloom-visibility-code-on"
    )[0] as HTMLElement;
    const langName = editable?.getAttribute("data-languagetipcontent");
    // and these for text boxes
    if (editable) {
        menuOptions.unshift(
            {
                l10nId: "EditTab.Toolbox.ComicTool.Options.Format",
                english: "Format",
                onClick: () => GetEditor().runFormatDialog(editable),
                icon: (
                    <img
                        css={css`
                            width: 19px;
                        `}
                        src="/bloom/bookEdit/img/cog.svg"
                    />
                )
            },
            {
                l10nId: "EditTab.Toolbox.ComicTool.Options.CopyText",
                english: "Copy Text",
                onClick: () => copySelection(),
                icon: <CopyIcon css={muiMenIconCss} />
            },
            {
                l10nId: "EditTab.Toolbox.ComicTool.Options.PasteText",
                english: "Paste Text",
                // We don't actually know there's no image on the clipboard, but it's not relevant for a text box.
                onClick: () => pasteClipboard(false),
                icon: <PasteIcon css={muiMenIconCss} />
            },
            {
                l10nId: "-",
                english: "",
                onClick: () => {}
            },
            {
                l10nId: "EditTab.Toolbox.ComicTool.Options.AutoHeight",
                english: "Auto Height",
                // We don't actually know there's no image on the clipboard, but it's not relevant for a text box.
                onClick: () => toggleAutoHeight(),
                icon: autoHeight && <CheckIcon css={muiMenIconCss} />
            },
            {
                l10nId: "-",
                english: "",
                onClick: () => {}
            }
        );
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
                    justify-content: space-around;
                    align-items: start;
                    // with the fiddles we're doing to line things up, we need padding at top but not bottom
                    // for it to look even.
                    padding: 5px 10px 0px;
                    margin: 0 auto 0 auto;
                    width: fit-content;
                    // Not really sure what's going on here, since none of the buttons contans text
                    // But somehow they have a tendency to be several pixels higher thant the contained
                    // icons, and this seems to be related to line-height. I don't want to set it
                    // to zero, in case (in some language) the tooltips wrap. But this seems to be small enough
                    // to prevent the problem.
                    line-height: 0.8em;
                    button {
                        line-height: 0.7em;
                    }
                    // needed because it's a child of #overlay-context-controls which has pointer-events:none
                    pointer-events: all;
                `}
            >
                {hasImage && (
                    <Fragment>
                        {
                            // latest card says we don't want this as a button ever.
                            // But I think it's worth keeping the code around a bit longer.
                            // isPlaceHolder || (
                            //     <BloomTooltip
                            //         id="metadata"
                            //         placement="top"
                            //         tip={{
                            //             l10nKey: "EditTab.Image.EditMetadata"
                            //         }}
                            //     >
                            //         <button
                            //             css={
                            //                 hasLicenseProblem
                            //                     ? svgIconCss
                            //                     : materialIconCss
                            //             }
                            //             onClick={runMetadataDialog}
                            //         >
                            //             {hasLicenseProblem ? (
                            //                 <img src="/bloom/bookEdit/img/Missing Metadata.svg" />
                            //             ) : (
                            //                 <CopyrightIcon color="primary" />
                            //             )}
                            //         </button>
                            //     </BloomTooltip>
                            // )
                        }
                        {// Choose image is only a LIKELY choice if we don't yet have one.
                        isPlaceHolder && (
                            <BloomTooltip
                                id="chooseImage"
                                placement="top"
                                tip={{
                                    l10nKey: "EditTab.Image.ChooseImage"
                                }}
                            >
                                <button
                                    css={materialIconCss()}
                                    onClick={e => {
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
                                >
                                    <SearchIcon color="primary" />
                                </button>
                            </BloomTooltip>
                        )}
                        {isPlaceHolder && (
                            <BloomTooltip
                                id="pasteImage"
                                placement="top"
                                tip={{
                                    l10nKey: "EditTab.Image.PasteImage"
                                }}
                            >
                                <button
                                    css={materialIconCss(1.3)}
                                    style={{ marginRight: "20px" }}
                                    onClick={e => {
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
                                >
                                    <PasteIcon color="primary" />
                                </button>
                            </BloomTooltip>
                        )}
                    </Fragment>
                )}
                {editable && (
                    <div
                        css={css`
                            display: flex;
                            flex-direction: column;
                            margin-right: 10px;
                        `}
                    >
                        <BloomTooltip
                            id="format"
                            placement="top"
                            tip={{
                                l10nKey:
                                    "EditTab.Toolbox.ComicTool.Options.Format"
                            }}
                        >
                            <button
                                css={svgIconCss}
                                style={{ width: "26px" }}
                                onClick={() => {
                                    if (!props.overlay) return;
                                    GetEditor().runFormatDialog(editable);
                                }}
                            >
                                <img
                                    // A trick to make it bloom-blue
                                    // To generate new filter rules like this, use https://codepen.io/sosuke/pen/Pjoqqp
                                    // It would be better still to make a react element SVG that can be any color.
                                    // But some uses of the icon are not in React, and I don't want it defined in two places
                                    // if we can help it.
                                    css={css`
                                        filter: invert(38%) sepia(93%)
                                            saturate(422%) hue-rotate(140deg)
                                            brightness(93%) contrast(96%);
                                        height: 21px !important;
                                        top: -1px;
                                    `}
                                    src="/bloom/bookEdit/img/cog.svg"
                                />
                            </button>
                        </BloomTooltip>
                        <div
                            css={css`
                                color: ${kBloomBlue};
                                font-size: 10px;
                                margin-bottom: 1px;
                            `}
                        >
                            {langName}
                        </div>
                    </div>
                )}
                {!hasVideo && (
                    <BloomTooltip
                        id="format"
                        placement="top"
                        tip={{
                            l10nKey:
                                "EditTab.Toolbox.ComicTool.Options.Duplicate"
                        }}
                    >
                        <button
                            css={svgIconCss}
                            onClick={() => {
                                if (!props.overlay) return;
                                makeDuplicateOfDragBubble();
                            }}
                        >
                            <img src="/bloom/bookEdit/img/Duplicate.svg" />
                        </button>
                    </BloomTooltip>
                )}
                {hasVideo && !videoAlreadyChosen && (
                    <Fragment>
                        <BloomTooltip
                            id="chooseVideo"
                            placement="top"
                            tip={{
                                l10nKey:
                                    "EditTab.Toolbox.ComicTool.Options.ChooseVideo"
                            }}
                        >
                            <button
                                css={svgIconCss}
                                onClick={() =>
                                    doVideoCommand(videoContainer, "choose")
                                }
                            >
                                <SearchIcon
                                    color="primary"
                                    viewBox="0 0 23 23" // a bit bigger
                                />
                            </button>
                        </BloomTooltip>
                        <BloomTooltip
                            id="recordVideo"
                            placement="top"
                            tip={{
                                l10nKey:
                                    "EditTab.Toolbox.ComicTool.Options.RecordYourself"
                            }}
                        >
                            <button
                                css={svgIconCss}
                                onClick={() =>
                                    doVideoCommand(videoContainer, "record")
                                }
                            >
                                <Circle
                                    color="primary"
                                    viewBox="0 0 29 29" // somewhat smaller
                                    css={css`
                                        top: 2px;
                                    `}
                                />
                            </button>
                        </BloomTooltip>
                    </Fragment>
                )}
                <BloomTooltip
                    id="trash"
                    placement="top"
                    tip={{
                        l10nKey: "Common.Delete"
                    }}
                >
                    <button
                        css={svgIconCss}
                        onClick={() => {
                            if (!props.overlay) return;
                            theOneBubbleManager?.deleteBubble();
                        }}
                    >
                        <TrashIcon
                            css={css`
                                height: 23px;
                                top: -2px;
                            `}
                            color={kBloomBlue}
                        />
                    </button>
                </BloomTooltip>
                <button
                    ref={ref => (menuEl.current = ref)}
                    css={materialIconCss()}
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
                        props.menuAnchorPosition ? "anchorPosition" : "anchorEl"
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
                                >
                                    {option.subMenu.map(
                                        (subOption, subIndex) => (
                                            <LocalizableMenuItem
                                                key={subIndex}
                                                l10nId={subOption.l10nId}
                                                english={subOption.english}
                                                onClick={subOption.onClick}
                                                icon={subOption.icon}
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
                                icon={option.icon}
                                variant="body1"
                                subLabelL10nId={option.subLabelL10nId}
                            />
                        );
                    })}
                </Menu>
            </div>
        </ThemeProvider>
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
