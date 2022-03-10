/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import FormGroup from "@material-ui/core/FormGroup";
import { ApiCheckbox } from "../../react_components/ApiCheckbox";
import { SettingsGroup } from "../commonPublish/BasePublishScreen";
import { useL10n } from "../../react_components/l10nHooks";
import { BloomApi } from "../../utils/bloomApi";
import { Div } from "../../react_components/l10nComponents";
import BloomButton from "../../react_components/bloomButton";
import { Button, FormControl, MenuItem, Select } from "@material-ui/core";
import Slider from "rc-slider";
import "../../bookEdit/css/rc-slider-bloom.less";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import AudioIcon from "@material-ui/icons/VolumeUp";
import { useEffect, useState } from "react";
import { format } from "path";
import { NoteBox } from "../../react_components/BloomDialog/commonDialogComponents";
interface IFormatItem {
    format: string; // to pass to BloomApi
    label: string;
    l10nKey: string; // for label
    dimension: string; // like 1280x720; assuming does not need localization
    codec: string; // like MP4 H.264; assuming does not need localization
    icon: React.ReactNode;
    firstLineOnly?: boolean;
}

export const VideoOptionsGroup: React.FunctionComponent<{
    pageDuration: number;
    onSetPageDuration: (arg: number) => void;
}> = props => {
    const [format, setFormat] = useState("facebook");
    const [tooBigMsg, setTooBigMsg] = useState("");
    useEffect(() => {
        BloomApi.get("publish/video/tooBigForScreenMsg", c => {
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
            label: "YouTube (full HD)",
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
                                font-size: smaller;
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
                                value={format}
                                onChange={e => {
                                    const newFormat = e.target.value as string;
                                    setFormat(newFormat);
                                    BloomApi.postString(
                                        "publish/video/format",
                                        newFormat
                                    );
                                }}
                                style={{ width: 160 }}
                                renderValue={f => {
                                    const item = formatItems.find(
                                        item => item.format === f
                                    )!;
                                    return (
                                        <VideoFormatItem
                                            {...item}
                                            firstLineOnly={true}
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
                        <NoteBox
                            css={css`
                                border: solid 1px ${kBloomBlue + "80"};
                            `}
                        >
                            {tooBigMsg}
                        </NoteBox>
                    )}
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
                                font-size: smaller;
                            `}
                        >
                            Turn pages without narration after:
                        </Div>
                        <Div
                            css={css`
                                text-align: center;
                                margin-bottom: 2px;
                                font-size: smaller;
                            `}
                            l10nKey="Common.Seconds"
                            l10nParam0={"" + props.pageDuration}
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
                                value={props.pageDuration}
                                step={0.5}
                                onChange={value =>
                                    props.onSetPageDuration(value)
                                }
                            />
                        </div>
                    </div>
                </FormControl>
            </FormGroup>
        </SettingsGroup>
    );
};

const VideoFormatItem: React.FunctionComponent<IFormatItem> = props => {
    return (
        <div>
            <div
                css={css`
                    display: flex;
                `}
            >
                {props.icon}
                <Div
                    l10nKey={props.l10nKey}
                    temporarilyDisableI18nWarning={true}
                    css={css`
                        font-size: smaller;
                        margin-left: 8px;
                    `}
                >
                    {props.label}
                </Div>
            </div>
            {props.firstLineOnly || (
                <Button
                    variant="contained"
                    color="primary"
                    css={css`
                        margin-left: 50px;
                        padding-top: 0 !important;
                        padding-bottom: 0 !important;
                        margin-bottom: 10px;
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
                </Button>
            )}
        </div>
    );
};
