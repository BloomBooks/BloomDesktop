/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import FormGroup from "@mui/material/FormGroup";
import { SettingsGroup } from "../commonPublish/PublishScreenBaseComponents";
import { useL10n } from "../../react_components/l10nHooks";
import { get, useApiBoolean, useApiData } from "../../utils/bloomApi";
import { Div } from "../../react_components/l10nComponents";
import { FormControl, MenuItem, Select, Typography } from "@mui/material";
import Slider from "@mui/material/Slider";
import "../../bookEdit/css/rc-slider-bloom.less";
import { kBloomBlue, kSelectCss } from "../../bloomMaterialUITheme";
import AudioIcon from "@mui/icons-material/VolumeUp";
import { useEffect, useState } from "react";
import { NoteBox } from "../../react_components/boxes";
import { BloomCheckbox } from "../../react_components/BloomCheckBox";
import { useSubscribeToWebSocketForObject } from "../../utils/WebSocketManager";
import { IFormatDimensionsResponseEntry } from "./IFormatDimensionsResponseEntry";
import { BloomTooltip } from "../../react_components/BloomToolTip";
import { kBloomDisabledOpacity } from "../../utils/colorUtils";

// The things that define each item in the Format menu
interface IFormatItem {
    format: string; // to pass to bloomApi
    label: string;
    l10nKey: string; // for label
    disabledL10nKey?: string; // for tooltip when disabled
    idealDimension: string; // like 1920x1080; assuming does not need localization
    actualDimension?: string | undefined; // like 1280x720; assuming does not need localization
    fileFormat: string; // like MP4; assuming does not need localization
    codec?: string | undefined; // like "H.264"; assuming does not need localization
    icon: React.ReactNode;
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
    const [motionEnabled] = useApiBoolean("publish/canHaveMotionMode", false);

    const [tooBigMsg, setTooBigMsg] = useState("");

    // When component is mounted, find out which formats should be disabled, then update the state.
    useEffect(() => {
        // Currently, only mp3 format can be disabled. Everything else is always enabled right now.
        get("publish/av/isMP3FormatSupported", c => {
            const isSupported = c.data as boolean;
            if (!isSupported) {
                setDisabledFormats(prevValue => prevValue.concat(["mp3"]));
            }
        });
    }, []);

    useEffect(() => {
        get("publish/av/tooBigForScreenMsg", c => {
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
        let changed = false;
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
    for (let i = 0; i < pageRange.length; i++) {
        const index = pageRange[i];
        marks[i] = { value: index, label: pageLabels[index] };
    }

    // Pretty-prints the resolution in a format like so: "1280x720 (16:9)"
    const prettyPrintResolution = (
        width: number,
        height: number,
        aspectRatio: string
    ) => {
        return `${width}x${height} (${aspectRatio})`;
    };

    const [formatItems, setFormatItems] = useState<IFormatItem[]>([
        {
            format: "facebook",
            label: "Facebook",
            l10nKey: "PublishTab.RecordVideo.Facebook",
            idealDimension: prettyPrintResolution(1280, 720, "16:9"),
            fileFormat: "MP4",
            codec: "H.264",
            icon: <img src="/bloom/images/facebook.png" height="16px" />
        },
        {
            format: "feature",
            label: "Feature Phone",
            l10nKey: "PublishTab.RecordVideo.FeaturePhone",
            idealDimension: prettyPrintResolution(352, 288, "5:4"),
            fileFormat: "3GP",
            codec: "H.263",
            icon: <img src="/bloom/images/featurephone.svg" height="16px" />
        },
        {
            format: "youtube",
            label: "YouTube",
            l10nKey: "PublishTab.RecordVideo.YouTube",
            idealDimension: prettyPrintResolution(1920, 1080, "16:9"),
            fileFormat: "MP4",
            codec: "H.264",
            icon: <img src="/bloom/images/youtube.png" height="16px" />
        },
        {
            format: "mp3",
            label: "Mp3 Audio",
            l10nKey: "PublishTab.RecordVideo.Mp3",
            disabledL10nKey: "PublishTab.RecordVideo.Mp3.Disabled",
            idealDimension: "64 kbps", // For audio, use the bitrate instead.
            fileFormat: "MP3",
            codec: undefined,
            icon: (
                <AudioIcon
                    css={css`
                        color: ${kBloomBlue};
                        font-size: 1rem; // seems to make it the same size as the 16-px icons.
                    `}
                />
            )
        }
    ]);

    const updatedFormatDimensionsList = useApiData<
        IFormatDimensionsResponseEntry[] | undefined
    >(`publish/av/getUpdatedFormatDimensions`, undefined);

    // Handle updates when we get the response back from the 'publish/av/getUpdatedFormatDimensions' API call.
    useEffect(() => {
        if (!updatedFormatDimensionsList) {
            // Nothing to do yet.
            return;
        }

        const newFormatItems = formatItems.map(item => {
            const newDimensions = updatedFormatDimensionsList.find(
                x => x.format === item.format
            );

            if (!newDimensions) {
                return item;
            }
            const newIdealDimensionStr = prettyPrintResolution(
                newDimensions.desiredWidth,
                newDimensions.desiredHeight,
                newDimensions.aspectRatio
            );

            const isActualDifferentThanIdeal =
                newDimensions.actualWidth !== newDimensions.desiredWidth ||
                newDimensions.actualHeight !== newDimensions.desiredHeight;

            const newActualDimensionStr = isActualDifferentThanIdeal
                ? prettyPrintResolution(
                      newDimensions.actualWidth,
                      newDimensions.actualHeight,
                      newDimensions.aspectRatio
                  )
                : undefined;

            const isItemChanged =
                newIdealDimensionStr !== item.idealDimension ||
                newActualDimensionStr !== item.actualDimension;
            if (!isItemChanged) {
                // Unchanged, return existing copy
                return item;
            } else {
                // Changed, make a copy. (Shallow copy is fine)
                const newItem = { ...item };
                newItem.idealDimension = newIdealDimensionStr;
                newItem.actualDimension = newActualDimensionStr;
                return newItem;
            }
        });

        setFormatItems(newFormatItems);
    }, [updatedFormatDimensionsList]);

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
                                        ${kSelectCss}
                                    `}
                                    variant="outlined"
                                    value={props.format}
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
                                                disabled={disabledFormats.includes(
                                                    item.format
                                                )}
                                                {...item}
                                            />
                                        );
                                    }}
                                >
                                    {formatItems.map(item => {
                                        return (
                                            <MenuItem
                                                value={item.format}
                                                key={item.format}
                                                // disabled=... we are doing our own disabling because letting the MenuItem do it hides our tooltip
                                            >
                                                <VideoFormatItem
                                                    disabled={disabledFormats.includes(
                                                        item.format
                                                    )}
                                                    {...item}
                                                />
                                            </MenuItem>
                                        );
                                    })}
                                </Select>
                            </div>
                        </div>
                        {tooBigMsg && <NoteBox>{tooBigMsg}</NoteBox>}
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
                                        size="small"
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
                                <BloomCheckbox
                                    label="Motion Book"
                                    l10nKey="PublishTab.Android.MotionBookMode"
                                    checked={props.motion}
                                    onCheckChanged={props.onMotionChange}
                                ></BloomCheckbox>
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
                                            const newVal = value as number[];
                                            if (
                                                newVal[0] != pageRange[0] ||
                                                newVal[1] != pageRange[1]
                                            ) {
                                                setPageRange(newVal);
                                            }
                                        }}
                                        marks={marks}
                                        size="small"
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

const VideoFormatItem: React.FunctionComponent<IFormatItem & {
    disabled: boolean;
}> = props => {
    const id = "mouse-over-popover-" + props.format;

    return (
        <BloomTooltip
            id={id}
            placement="left"
            showDisabled={props.disabled}
            tipWhenDisabled={{
                english: "unused",
                l10nKey: props.disabledL10nKey!
            }}
            tip={
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                        align-items: center;
                    `}
                >
                    <div>
                        {props.actualDimension
                            ? `Ideal: ${props.idealDimension}`
                            : props.idealDimension}
                    </div>
                    {props.actualDimension && (
                        <div>Actual: {props.actualDimension}</div>
                    )}
                    <div>{props.fileFormat}</div>
                    {props.codec && <div>{props.codec}</div>}
                </div>
            }
        >
            <div
                css={css`
                    display: flex;
                    min-width: 155px;
                    opacity: ${props.disabled ? kBloomDisabledOpacity : 1};
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
