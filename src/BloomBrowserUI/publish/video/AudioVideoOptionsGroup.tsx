/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import FormGroup from "@material-ui/core/FormGroup";
import { SettingsGroup } from "../commonPublish/BasePublishScreen";
import { useL10n } from "../../react_components/l10nHooks";
import { BloomApi } from "../../utils/bloomApi";
import { Div } from "../../react_components/l10nComponents";
import {
    FormControl,
    MenuItem,
    Popover,
    Select,
    Typography
} from "@material-ui/core";
import Slider from "rc-slider";
import "../../bookEdit/css/rc-slider-bloom.less";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import AudioIcon from "@material-ui/icons/VolumeUp";
import { useEffect, useState } from "react";
import { NoteBox } from "../../react_components/BloomDialog/commonDialogComponents";

// The things that define each item in the Format menu
interface IFormatItem {
    format: string; // to pass to BloomApi
    label: string;
    l10nKey: string; // for label
    dimension: string; // like 1280x720; assuming does not need localization
    codec: string; // like MP4 H.264; assuming does not need localization
    icon: React.ReactNode;
}

// Props for the VideoFormatOptions component, which displays an instance of IFormatItem
interface IProps extends IFormatItem {
    // The VideoFormatItem may (when hovered over) display a popup with more details.
    // Usually the VFM controls for itself whether this is shown; but we also permit
    // this behavior to work in the controlled component mode, where the client controls
    // its visibility. Technically, the appearance of the popup is controlled by keeping
    // track of which component it is anchored to (if visible), or storing a null if it
    // isn't. If the component is controlled, the client provides changePopupAnchor to
    // receive notification that the control wishes to change this (because it is hovered over),
    // and popupAnchorElement to actually control the presence (and placement) of the popup.
    // The client should normally change popupAnchorElement to whatever changePopupAnchor
    // tell it to, but may also set it to null to force the popup closed. It probably doesn't
    // make sense to set it to anything other than a value received from changePopupAnchor or null.
    changePopupAnchor?: (anchor: HTMLElement | null) => void;
    popupAnchorElement?: HTMLElement | null;
}

export const AudioVideoOptionsGroup: React.FunctionComponent<{
    pageTurnDelay: number;
    onSetPageTurnDelay: (arg: number) => void;
    format: string;
    setFormat: (f: string) => void;
}> = props => {
    const format = props.format;
    const setFormat = props.setFormat;
    const [tooBigMsg, setTooBigMsg] = useState("");
    // Manages visibility of the details popup for the main Format label (that shows in the
    // control when the dropdown is closed).
    const [
        formatPopupAnchor,
        setFormatPopupAnchor
    ] = useState<HTMLElement | null>(null);
    // Using this we make the appearance of the dropdown 'controlled', not because
    // we want independent control over the dropdown, but so we can hide the popup details
    // thing when we show the dropdown.
    const [formatDropdownIsOpen, setFormatDropdownIsOpen] = useState(false);
    useEffect(() => {
        BloomApi.get("publish/av/tooBigForScreenMsg", c => {
            setTooBigMsg(c.data);
        });
    }, [format]);

    const formatItems: IFormatItem[] = [
        {
            format: "facebook",
            label: "Facebook",
            l10nKey: "PublishTab.RecordVideo.Facebook",
            dimension: "1280x720",
            codec: "MP4 H.264",
            icon: <img src="facebook.png" height="16px" />
        },
        {
            format: "feature",
            label: "Feature Phone",
            l10nKey: "PublishTab.RecordVideo.FeaturePhone",
            dimension: "352x288",
            codec: "3GP H.263",
            icon: <img src="featurephone.svg" height="16px" />
        },
        {
            format: "youtube",
            label: "YouTube",
            l10nKey: "PublishTab.RecordVideo.YouTube",
            dimension: "1920x1080",
            codec: "MP4 H.264",
            icon: <img src="youtube.png" height="16px" />
        },
        {
            format: "mp3",
            label: "Mp3 Audio",
            l10nKey: "PublishTab.RecordVideo.Mp3",
            dimension: "no video",
            codec: "MP3",
            icon: (
                <AudioIcon
                    css={css`
                        color: ${kBloomBlue};
                        font-size: 1rem; // seems to make it the same size as the 16-px icons.
                    `}
                />
            )
        }
    ];

    return (
        <SettingsGroup label={useL10n("Options", "Common.Options")}>
            <FormGroup>
                <FormControl variant="outlined">
                    <Typography component={"div"}>
                        <div
                            css={css`
                                display: flex;
                                margin-top: 20px;
                                .MuiSelect-root {
                                    padding-top: 3px !important;
                                    padding-bottom: 4px !important;
                                }
                            `}
                        >
                            <Div
                                css={css`
                                    font-weight: bold;
                                `}
                                l10nKey="PublishTab.RecordVideo.Format"
                                temporarilyDisableI18nWarning={true}
                            >
                                Format
                            </Div>

                            <div
                                css={css`
                                    margin-left: 20px;
                                `}
                            >
                                <Select
                                    css={css`
                                        background-color: white;
                                        &.MuiOutlinedInput-root {
                                            border-radius: 0 !important;

                                            .MuiOutlinedInput-notchedOutline {
                                                border-width: 1px !important;
                                                border-color: ${kBloomBlue} !important; // it usually is anyway, but not before MUI decides to focus it.
                                            }
                                        }
                                    `}
                                    value={format}
                                    open={formatDropdownIsOpen}
                                    onOpen={() => {
                                        setFormatPopupAnchor(null);
                                        setFormatDropdownIsOpen(true);
                                    }}
                                    onClose={() =>
                                        setFormatDropdownIsOpen(false)
                                    }
                                    onChange={e => {
                                        const newFormat = e.target
                                            .value as string;
                                        setFormat(newFormat);
                                    }}
                                    style={{ width: 160 }}
                                    renderValue={f => {
                                        const item = formatItems.find(
                                            item => item.format === f
                                        )!;
                                        return (
                                            <VideoFormatItem
                                                {...item}
                                                popupAnchorElement={
                                                    formatPopupAnchor
                                                }
                                                changePopupAnchor={
                                                    setFormatPopupAnchor
                                                }
                                            />
                                        );
                                    }}
                                >
                                    {formatItems.map(item => {
                                        return (
                                            <MenuItem
                                                value={item.format}
                                                key={item.format}
                                            >
                                                <VideoFormatItem {...item} />
                                            </MenuItem>
                                        );
                                    })}
                                </Select>
                            </div>
                        </div>
                        {tooBigMsg && (
                            <NoteBox addBorder={true}>{tooBigMsg}</NoteBox>
                        )}
                        {/** The below div is disabled for MP3 because currently, we ignore this setting and immediately flip pages with no narration in mp3 mode.
                         * That's because, in the context of making an mp3, it doesn't make much sense to spend time on pages with no audio at all, especially x-matter pages.
                         * Pages with background music but no narration are a trickier case. We think usually it won't be valuable to linger on them, but there could be some exceptions.
                         * For now, we're keeping it simple and immediately flipping those pages too.
                         */}
                        {format !== "mp3" && (
                            <div
                                css={css`
                                    padding-right: 30px;
                                `}
                            >
                                <Div
                                    l10nKey="PublishTab.RecordVideo.TurnPageAfter"
                                    temporarilyDisableI18nWarning={true}
                                    css={css`
                                        margin-bottom: 2px;
                                        margin-top: 20px;
                                        font-weight: bold;
                                    `}
                                >
                                    Turn pages without narration after:
                                </Div>
                                <Div
                                    css={css`
                                        text-align: center;
                                        margin-bottom: 2px;
                                    `}
                                    l10nKey="Common.Seconds"
                                    l10nParam0={props.pageTurnDelay.toString()}
                                    temporarilyDisableI18nWarning={true}
                                >
                                    %0 seconds
                                </Div>
                                <div
                                    className="bgSliderWrapper"
                                    css={css`
                                        .rc-slider-rail {
                                            background-color: #ddd !important;
                                        }
                                        .rc-slider-handle {
                                            background-color: ${kBloomBlue} !important;
                                        }
                                        .rc-slider-dot {
                                            background-color: ${kBloomBlue} !important;
                                            border-color: white !important; // should match background, I think.
                                        }
                                        .rc-slider-track {
                                            background-color: ${kBloomBlue}60 !important;
                                        }
                                    `}
                                >
                                    <Slider
                                        max={10}
                                        min={1}
                                        value={props.pageTurnDelay}
                                        step={0.5}
                                        onChange={value =>
                                            props.onSetPageTurnDelay(value)
                                        }
                                    />
                                </div>
                            </div>
                        )}
                    </Typography>
                </FormControl>
            </FormGroup>
        </SettingsGroup>
    );
};

const VideoFormatItem: React.FunctionComponent<IProps> = props => {
    // controls visibility and placement of the detail popup element when we are not
    // being used in controlled mode. Ignored if props.changePopupAnchor is defined.
    const [localAnchorEl, setAnchorEl] = React.useState<HTMLElement | null>(
        null
    );

    // Handle an event that should open the detail popover.
    const handlePopoverOpen = (event: React.MouseEvent<HTMLElement>) => {
        if (props.changePopupAnchor) {
            props.changePopupAnchor(event.currentTarget);
        } else {
            setAnchorEl(event.currentTarget);
        }
    };

    const anchorEl = props.changePopupAnchor
        ? props.popupAnchorElement
        : localAnchorEl;

    // Handle an event that should close the detail popover.
    const handlePopoverClose = () => {
        if (props.changePopupAnchor) {
            props.changePopupAnchor(null);
        } else {
            setAnchorEl(null);
        }
    };

    const open = Boolean(anchorEl);
    const id = "mouse-over-popover-" + props.format;

    const arrowSize = 8;
    const popupColor = kBloomBlue;

    return (
        <div>
            <div
                css={css`
                    display: flex;
                    min-width: 155px;
                `}
                aria-owns={open ? id : undefined}
                aria-haspopup="true"
                onMouseEnter={handlePopoverOpen}
                onMouseLeave={handlePopoverClose}
            >
                {props.icon}
                <Div
                    l10nKey={props.l10nKey}
                    temporarilyDisableI18nWarning={true}
                    css={css`
                        margin-left: 8px;
                    `}
                >
                    {props.label}
                </Div>
                <Popover
                    id={id}
                    css={css`
                        // This is just an informational popover, we don't need to suppress events outside it.
                        // Even more importantly, we don't want to prevent the parent control from receiving
                        // the mouse-move events that would indicate the mouse is no longer over the anchor
                        // and so the popover should be removed!
                        pointer-events: none;
                        .MuiPopover-paper {
                            // This allows the arrow to be seen. (If instead we try to make the arrow be
                            // inside the main content area of the popover, it is impossible to get the
                            // right background color to make the area either side of the arrow look right.
                            // The popover div is added at the root level so that the whole thing doesn't
                            // get clipped; therefore, a transparent background doesn't 'see' the thing that
                            // it seems, visibly, to be on top of. And the background is very variable, as it
                            // might be over a selected item, an unselected item, the shadow that gets created
                            // around the popover, a combination of the above...)
                            overflow: visible !important;
                        }
                    `}
                    // This might be a better way to do it in material-ui 5? Not in V4 API, but in MUI examples.
                    // sx={{
                    //     pointerEvents: 'none',
                    //   }}
                    open={open}
                    anchorEl={anchorEl}
                    anchorOrigin={{
                        vertical: "bottom",
                        horizontal: "right"
                    }}
                    transformOrigin={{
                        // 15 pixels below the bottom (based on anchorOrigin) of the anchor;
                        // leaves room for arrow and a bit of margin.
                        vertical: -15,
                        horizontal: "right"
                    }}
                    onClose={handlePopoverClose}
                    disableRestoreFocus // most MUI examples have this, not sure what it does.
                >
                    <div
                        css={css`
                            position: relative;
                        `}
                    >
                        <div
                            css={css`
                                // This div makes the arrow.
                                // I have not made it smart enough to move around if popover
                                // gets smart and places the popover in an unexpected place
                                // (e.g., so it fits in the window). This control is used in a small
                                // enough screen region that I don't think this is likely to happen.
                                border: solid ${arrowSize}px;
                                position: absolute;
                                top: ${1 - 2 * arrowSize}px;
                                left: calc(50% - ${arrowSize / 2}px);
                                border-color: transparent;
                                border-bottom-color: ${popupColor};
                            `}
                        ></div>
                        <div
                            css={css`
                                background-color: ${popupColor};
                                color: white;
                                border-radius: 4px;
                                padding: 4px 8px;
                                position: relative;
                            `}
                        >
                            <div
                                css={css`
                                    display: flex;
                                    flex-direction: column;
                                    justify-content: center;
                                `}
                            >
                                <div>{props.dimension}</div>
                                <div>{props.codec}</div>
                            </div>
                        </div>
                    </div>
                </Popover>
            </div>
        </div>
    );
};
