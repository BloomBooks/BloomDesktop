/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import FormGroup from "@material-ui/core/FormGroup";
import { SettingsGroup } from "../commonPublish/BasePublishScreen";
import { useL10n } from "../../react_components/l10nHooks";
import { BloomApi } from "../../utils/bloomApi";
import { Div } from "../../react_components/l10nComponents";
import { FormControl, MenuItem, Select, Typography } from "@material-ui/core";
import Slider from "@material-ui/core/Slider";
import "../../bookEdit/css/rc-slider-bloom.less";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import AudioIcon from "@material-ui/icons/VolumeUp";
import { useEffect, useState } from "react";
import { NoteBox } from "../../react_components/BloomDialog/commonDialogComponents";
import { BloomTooltip } from "../../react_components/BloomToolTip";
import { MuiCheckbox } from "../../react_components/muiCheckBox";
import { useSubscribeToWebSocketForObject } from "../../utils/WebSocketManager";

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
    onFormatChanged: (f: string) => void;
    pageRange: number[];
    onSetPageRange: (arg: number[]) => void;
    motion: boolean;
    onMotionChange: (arg: boolean) => void;
}> = props => {
    // Stores which formats should be non-selectable.
    const [disabledFormats, setDisabledFormats] = useState<string[]>([]);
    const [motionEnabled] = BloomApi.useApiBoolean(
        "publish/android/canHaveMotionMode",
        false
    );

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

    // When component is mounted, find out which formats should be disabled, then update the state.
    useEffect(() => {
        // Currently, only mp3 format can be disabled. Everything else is always enabled right now.
        BloomApi.get("publish/av/isMP3FormatSupported", c => {
            const isSupported = c.data as boolean;
            if (!isSupported) {
                setDisabledFormats(prevValue => prevValue.concat(["mp3"]));
            }
        });
    }, []);

    useEffect(() => {
        BloomApi.get("publish/av/tooBigForScreenMsg", c => {
            setTooBigMsg(c.data);
        });
    }, [props.format]);
    const [pageLabels, setPageLabels] = useState([]);
    useSubscribeToWebSocketForObject("publishPageLabels", "ready", data =>
        setPageLabels((data as any).labels)
    );

    const setPageRange = (range: number[]) => {
        if (range[0] === 0 && range[1] === pageLabels.length - 1) {
            props.onSetPageRange([]);
        } else {
            props.onSetPageRange(range);
        }
    };

    const pageRange = Array.from(props.pageRange);
    if (pageLabels.length) {
        if (pageRange.length == 0) {
            // we want them all
            pageRange.push(0);
            pageRange.push(pageLabels.length - 1);
        }
        // validate pageRange and notify parent if it is corrected.
        // Needing to correct is normal since the initial guess is based on pages
        // in the original book, but the bloomPub version may well omit some.
        var changed = false;
        if (pageRange[1] >= pageLabels.length) {
            pageRange[1] = pageLabels.length - 1;
            changed = true;
        }
        if (pageRange[0] > pageRange[1]) {
            pageRange[0] = pageRange[1];
            changed = true;
        }
        if (changed) {
            setPageRange(pageRange);
        }
    }

    // "marks" are usually tick marks along the slider at equal intervals, but we're using
    // it unconventionally with just two marks that correspond to the thumb positions, to show the
    // corresponding page labels.
    const marks: Array<{ value: number; label: string }> = [];
    for (var i = 0; i < pageRange.length; i++) {
        var index = pageRange[i];
        marks[i] = { value: index, label: pageLabels[index] };
    }

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
            dimension: "64 kbps",
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

    useEffect(() => {
        // Ensure selection is not disabled
        if (disabledFormats.includes(props.format)) {
            const firstNonDisabledFormat = formatItems
                .map(x => x.format)
                .find(f => !disabledFormats.includes(f));

            if (firstNonDisabledFormat === undefined) {
                // If for some weird reason, all formats are disabled, just leave it at whatever format was originally selected.
                return;
            }

            props.onFormatChanged(firstNonDisabledFormat);
        }
    }, [props.format, disabledFormats]);

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
                                l10nComment="a heading to select which audio or video format to record"
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
                                    value={props.format}
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
                                        props.onFormatChanged(newFormat);
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
                                                disabled={disabledFormats.includes(
                                                    item.format
                                                )}
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
                        {props.format !== "mp3" && (
                            <div
                                css={css`
                                    // The label can extend well beyond the end of the slider, so we need some extra space.
                                    padding-right: 30px;
                                `}
                            >
                                <Div
                                    l10nKey="PublishTab.RecordVideo.TurnPageAfter"
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
                                    l10nComment="%0 is a number of seconds"
                                >
                                    %0 seconds
                                </Div>
                                <div className="bgSliderWrapper">
                                    <Slider
                                        max={10}
                                        min={1}
                                        value={props.pageTurnDelay}
                                        step={0.5}
                                        onChange={(event, value) =>
                                            props.onSetPageTurnDelay(
                                                value as number
                                            )
                                        }
                                    />
                                </div>
                            </div>
                        )}
                        {motionEnabled && (
                            <FormGroup
                                css={css`
                                    margin-top: 20px;
                                `}
                            >
                                <MuiCheckbox
                                    label="Motion Book"
                                    l10nKey="PublishTab.Android.MotionBookMode"
                                    checked={props.motion}
                                    onCheckChanged={props.onMotionChange}
                                ></MuiCheckbox>
                            </FormGroup>
                        )}
                        {/* We need the > 0 here to force a boolean...the number zero will actually be shown */}
                        {pageLabels.length > 0 && (
                            <React.Fragment>
                                <Div
                                    l10nKey="PublishTab.RecordVideo.RecordThesePages"
                                    css={css`
                                        margin-bottom: 2px;
                                        margin-top: 20px;
                                        font-weight: bold;
                                    `}
                                >
                                    Record These Pages:
                                </Div>
                                <div
                                    css={css`
                                        // The label can extend well beyond the end of the slider, so we need some extra space.
                                        padding-right: 30px;
                                    `}
                                >
                                    <Slider
                                        css={css`
                                            .MuiSlider-markLabel {
                                                max-width: 40px; // encourages wrapping, but long words can still overflow, and short ones center
                                                white-space: normal; // allows wrapping
                                                text-align: center;
                                            }
                                        `}
                                        max={pageLabels.length - 1}
                                        min={0}
                                        value={pageRange}
                                        step={1}
                                        onChange={(event, value) => {
                                            var newVal = value as number[];
                                            if (
                                                newVal[0] != pageRange[0] ||
                                                newVal[1] != pageRange[1]
                                            ) {
                                                setPageRange(newVal);
                                            }
                                        }}
                                        marks={marks}
                                    />
                                </div>
                            </React.Fragment>
                        )}
                    </Typography>
                </FormControl>
            </FormGroup>
        </SettingsGroup>
    );
};

const VideoFormatItem: React.FunctionComponent<IProps> = props => {
    const id = "mouse-over-popover-" + props.format;
    const popupColor = kBloomBlue;

    return (
        <BloomTooltip
            id={id}
            tooltipBackColor={popupColor}
            popupAnchorElement={props.popupAnchorElement}
            changePopupAnchor={props.changePopupAnchor}
            tooltipContent={
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                        align-items: center;
                    `}
                >
                    <div>{props.dimension}</div>
                    <div>{props.codec}</div>
                </div>
            }
        >
            <div
                css={css`
                    display: flex;
                    min-width: 155px;
                `}
            >
                {props.icon}
                <Div
                    l10nKey={props.l10nKey}
                    css={css`
                        margin-left: 8px;
                    `}
                    key={props.l10nKey} // prevents stale labels (BL-11179)
                >
                    {props.label}
                </Div>
            </div>
        </BloomTooltip>
    );
};
