/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import FormGroup from "@mui/material/FormGroup";
import { ApiCheckbox } from "../../react_components/ApiCheckbox";
import { SettingsGroup } from "../commonPublish/PublishScreenBaseComponents";
import { useL10n } from "../../react_components/l10nHooks";
import {
    get,
    getBoolean,
    post,
    useApiBoolean,
    useApiString,
    useWatchString
} from "../../utils/bloomApi";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import { TickableBox } from "../../react_components/tickableBox";
import { ILanguagePublishInfo } from "./PublishLanguagesGroup";
import { Link as MuiLink } from "@mui/material";
import { ActivityIcon } from "../../react_components/icons/ActivityIcon";
import { TalkingBookIcon } from "../../react_components/icons/TalkingBookIcon";
import { SignLanguageIcon } from "../../react_components/icons/SignLanguageIcon";
import { kBloomDisabledOpacity } from "../../utils/colorUtils";
import { MotionIcon } from "../../react_components/icons/MotionIcon";
import { ComicIcon } from "../../react_components/icons/ComicIcon";
import { VisuallyImpairedIcon } from "../../react_components/icons/VisuallyImpairedIcon";

export const PublishFeaturesGroup: React.FunctionComponent<{
    onChange?: () => void;
    generation?: number; // bump this to force recalc of computed features
}> = props => {
    const [motionEnabled] = useApiBoolean("publish/canHaveMotionMode", false);
    const [hasActivities] = useApiBoolean("publish/hasActivities", false);
    const [comicEnabled] = useApiBoolean("publish/comicEnabled", false);
    const [visuallyImpairedEnabled] = useApiBoolean(
        "publish/visuallyImpairedEnabled",
        false
    );
    const [langs, setLangs] = React.useState<ILanguagePublishInfo[]>([]);
    React.useEffect(() => {
        get(
            "publish/languagesInBook",

            // onSuccess
            result => {
                let newLangs = result.data;
                // This is for debugging. When all is well, the JSON gets parsed automatically.
                // If there's a syntax error in the JSON, result.data is just the string.
                // Trying to parse it ourselves at least gets the syntax error into our log/debugger.
                if (!newLangs.map) {
                    newLangs = JSON.parse(newLangs);
                }

                // Note that these are just simple objects with fields, not instances of classes with methods.
                // That's why these are ILanguagePublishInfo's (interface) instead of LanguagePublishInfo's (class)
                setLangs(newLangs as ILanguagePublishInfo[]);
            }

            // onError
            // Currently just ignoring errors... letting BloomServer take care of reporting anything that comes up
            // () => {
            // }
        );
    }, [props.generation]);
    const [hasVideo] = useApiBoolean("publish/hasVideo", false);

    const showSignLanguageChooser = () => {
        post("publish/chooseSignLanguage");
    };

    const signLanguageNameOriginal = useApiString(
        "publish/signLanguageName",
        ""
    );
    const signLanguageName = useWatchString(
        signLanguageNameOriginal,
        "publish",
        "signLang"
    );

    const l1Name = useApiString("publish/l1Name", "");

    const signLanguageEnabled = hasVideo && !!signLanguageName;

    const isTalkingBook = langs.some(
        item => item.includeText && item.containsAnyAudio && item.includeAudio
    );

    const couldBeTalkingBook = langs.some(item => item.containsAnyAudio);
    const noNarration = useL10n(
        "This book has no recorded narration",
        "PublishTab.Feature.TallkingBook.NoNarration"
    );
    const noNarrationSelected = useL10n(
        "No talking book languages are selected",
        "PublishTab.Feature.TallkingBook.NoNarrationSelected"
    );
    const tbTitle = isTalkingBook
        ? ""
        : couldBeTalkingBook
        ? noNarrationSelected
        : noNarration;

    const slPresent = useL10n(
        "The videos in this book contain {N}",
        "PublishTab.Feature.SignLanguage.Present"
    );
    const slNoVideo = useL10n(
        "No videos found in this book",
        "PublishTab.Feature.SignLanguage.NoVideos"
    );
    const slUnknown = useL10n(
        "This collection has {no sign language selected}",
        "PublishTab.Feature.SignLanguage.Unknown"
    );
    let slTitle: React.ReactNode = "";
    if (signLanguageEnabled) {
        // Break it up into the bit before {N} and the bit after.
        // Replace {N} with a link that shows the sign language name and opens the chooser.
        const match = slPresent.match(/^([^{]*)\{N\}(.*)$/);
        if (match) {
            slTitle = (
                <React.Fragment>
                    {match[1]}
                    <MuiLink
                        css={css`
                            font-size: 11px;
                        `}
                        onClick={showSignLanguageChooser}
                    >
                        {signLanguageName}
                    </MuiLink>
                    {match[2]}
                </React.Fragment>
            );
        } else {
            slTitle = slPresent;
        }
    } else {
        if (hasVideo) {
            // Sign language must be unspecified.
            // Break the string up into the bit before whatever is in braces, the bit inside, and the bit after.
            // Turn whatever is inside into a link to launch the chooser.
            const match = slUnknown.match(/^([^{]*)\{([^}]*)\}(.*)$/);
            if (match) {
                slTitle = (
                    <React.Fragment>
                        {match[1]}
                        <MuiLink
                            css={css`
                                font-size: 11px;
                            `}
                            onClick={showSignLanguageChooser}
                        >
                            {match[2]}
                        </MuiLink>
                        {match[3]}
                    </React.Fragment>
                );
            } else {
                slTitle = slUnknown;
            }
        } else {
            slTitle = slNoVideo;
        }
    }

    const noActivitiesTitle = useL10n(
        "No activities were found in this book",
        "PublishTab.Feature.Activities.None"
    );
    const hasActivitiesTitle = useL10n(
        "The book has activities",
        "PublishTab.Feature.Activities.Present"
    );
    const activitiesTitle = hasActivities
        ? hasActivitiesTitle
        : noActivitiesTitle;

    const noComicTitle = useL10n(
        "No overlays that might make this a Comic book were found",
        "PublishTab.Feature.Comic.None"
    );
    const hasComicTitle = useL10n(
        "This is a comic book",
        "PublishTab.Feature.comic.Possible"
    );
    const comicTitle = comicEnabled ? hasComicTitle : noComicTitle;

    const noMotionTitle = useL10n(
        "No motion settings were found in this book",
        "PublishTab.Feature.Motion.None"
    );
    const hasMotionTitle = useL10n(
        "Publish this book to show motion in landscape mode",
        "PublishTab.Feature.Motion.Possible"
    );
    const motionTitle = motionEnabled ? hasMotionTitle : noMotionTitle;

    const noVisionTitle = useL10n(
        "No image descriptions were found in this book",
        "PublishTab.Feature.Vision.None"
    );
    const hasVisionTitle = useL10n(
        "This book is usable by people whose vision is impaired",
        "PublishTab.Feature.Vision.Possible"
    );
    const visionTitle = visuallyImpairedEnabled
        ? hasVisionTitle
        : noVisionTitle;

    return (
        <SettingsGroup
            label={useL10n("Features", "PublishTab.Android.Features")}
        >
            <FormGroup>
                <TickableBox
                    english="Talking Book"
                    l10nKey="PublishTab.TalkingBook"
                    icon={<TalkingBookIcon color={kBloomBlue} />}
                    title={tbTitle}
                    disabled={!isTalkingBook}
                    ticked={isTalkingBook}
                />
                <ApiCheckbox
                    css={css`
                        .MuiCheckbox-Root {
                            padding-top: 0;
                        }
                    `}
                    english="Sign Language"
                    l10nKey="PublishTab.Upload.SignLanguage"
                    apiEndpoint="publish/signLanguage"
                    icon={
                        <SignLanguageIcon
                            css={css`
                                height: 20px;
                                align-self: center;
                                // I would prefer if this happened automatically as a result of the check box
                                // being disabled. But that currently just modifies the css color. I don't
                                // think there is a way to make the SVG be displayed using the current
                                // CSS foreground color.
                                ${signLanguageEnabled
                                    ? ""
                                    : `opacity: ${kBloomDisabledOpacity};`}
                            `}
                            color={kBloomBlue}
                        />
                    }
                    title={slTitle}
                    disabled={!signLanguageEnabled}
                />
                <TickableBox
                    english="Activity"
                    l10nKey="PublishTab.Activity"
                    icon={
                        <ActivityIcon
                            css={css`
                                height: 20px;
                                width: 20px;
                            `}
                            color={kBloomBlue}
                        />
                    }
                    title={activitiesTitle}
                    disabled={!hasActivities}
                    ticked={hasActivities}
                />
                <ApiCheckbox
                    css={css`
                        .MuiCheckbox-Root {
                            padding-top: 0;
                        }
                    `}
                    english="Comic"
                    l10nKey="PublishTab.Comic"
                    apiEndpoint="publish/comic"
                    icon={
                        <ComicIcon
                            css={css`
                                height: 20px;
                                width: 20px;
                                align-self: center;
                                ${comicEnabled
                                    ? ""
                                    : `opacity: ${kBloomDisabledOpacity};`}
                            `}
                            color={kBloomBlue}
                        />
                    }
                    title={comicTitle}
                    disabled={!comicEnabled}
                />
                <ApiCheckbox
                    english="Motion Book"
                    l10nKey="PublishTab.Android.MotionBookMode"
                    // tslint:disable-next-line:max-line-length
                    l10nComment="Motion Books are Talking Books in which the picture fills the screen, then pans and zooms while you hear the voice recording. This happens only if you turn the book sideways."
                    apiEndpoint="publish/motionBookMode"
                    icon={<MotionIcon color={kBloomBlue} />}
                    title={motionTitle}
                    // This causes the preview to be regenerated...the only feature that actually affects the
                    // preview results.
                    onChange={props.onChange}
                    disabled={!motionEnabled}
                />
                <ApiCheckbox
                    english="Accessible to the Visually Impaired in %0"
                    l10nKey="PublishTab.AccessibleVisually"
                    l10nParam0={l1Name}
                    apiEndpoint="publish/visuallyImpaired"
                    icon={
                        <VisuallyImpairedIcon
                            css={css`
                                height: 20px;
                                width: 20px;
                                align-self: start;
                                ${visuallyImpairedEnabled
                                    ? ""
                                    : `opacity: ${kBloomDisabledOpacity};`}
                            `}
                            color={kBloomBlue}
                        />
                    }
                    title={visionTitle}
                    disabled={!visuallyImpairedEnabled}
                />
            </FormGroup>
        </SettingsGroup>
    );
};
