/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import FormGroup from "@mui/material/FormGroup";
import { ApiCheckbox } from "../../react_components/ApiCheckbox";
import { SettingsGroup } from "../commonPublish/PublishScreenBaseComponents";
import { useL10n } from "../../react_components/l10nHooks";
import {
    get,
    post,
    useApiBoolean,
    useApiString,
    useWatchString
} from "../../utils/bloomApi";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import { ILanguagePublishInfo } from "./PublishLanguagesGroup";
import { Link as MuiLink } from "@mui/material";
import { ActivityIcon } from "../../react_components/icons/ActivityIcon";
import { TalkingBookIcon } from "../../react_components/icons/TalkingBookIcon";
import { SignLanguageIcon } from "../../react_components/icons/SignLanguageIcon";
import { MotionIcon } from "../../react_components/icons/MotionIcon";
import { ComicIcon } from "../../react_components/icons/ComicIcon";
import { VisuallyImpairedIcon } from "../../react_components/icons/VisuallyImpairedIcon";
import { BloomCheckbox } from "../../react_components/BloomCheckBox";
import { BloomTooltip } from "../../react_components/BloomToolTip";
import { useEnterpriseAvailable } from "../../react_components/requiresBloomEnterprise";

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
    const enterpriseAvailable = useEnterpriseAvailable();
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
    const NoRecordings = useL10n(
        "This is disabled because this book does not have any Talking Book recordings.",
        "PublishTab.Feature.TalkingBook.NoRecordings"
    );
    const NoLanguagesSelected = useL10n(
        "This is disabled because no “Talking Book Languages” are selected.",
        "PublishTab.Feature.TalkingBook.NoLanguagesSelected"
    );
    const talkingBookTooltip = isTalkingBook
        ? ""
        : couldBeTalkingBook
        ? NoLanguagesSelected
        : NoRecordings;

    const slPresent = useL10n(
        "The videos in this book contain {N}",
        "PublishTab.Feature.SignLanguage.Present"
    );
    const slNoVideo = useL10n(
        "This is disabled because this book does not have any videos.",
        "PublishTab.Feature.SignLanguage.NoVideos"
    );
    const slUnknown = useL10n(
        "This is disabled because this collection has {no sign language selected}.",
        "PublishTab.Feature.SignLanguage.Unknown"
    );
    let slTooltip: React.ReactElement | string = slNoVideo; // default
    let linkPart = "";
    let beforePart = "";
    let afterPart = "";
    if (signLanguageEnabled) {
        linkPart = slPresent; // default if translator messes up
        // Break it up into the bit before {N} and the bit after.
        // Replace {N} with a link that shows the sign language name and opens the chooser.
        const match = slPresent.match(/^([^{]*)\{N\}(.*)$/);
        if (match) {
            beforePart = match[1];
            linkPart = signLanguageName;
            afterPart = match[2];
        }
    } else {
        if (hasVideo) {
            // Sign language must be unspecified.
            linkPart = slUnknown; // default if translator messes up
            // Break the string up into the bit before whatever is in braces, the bit inside, and the bit after.
            // Turn whatever is inside into a link to launch the chooser.
            const match = slUnknown.match(/^([^{]*)\{([^}]*)\}(.*)$/);
            if (match) {
                beforePart = match[1];
                linkPart = match[2];
                afterPart = match[3];
            }
        }
    }
    // Note: if we need to copy this to use elsewhere, try to figure how to make a component.
    // It may be helpful to make use of the ability to name RegEx groups, and tie a particular
    // named group to a particular click action. However, it will be difficult to generalize
    // to more than one link because translation might change the order.
    if (linkPart) {
        slTooltip = (
            <React.Fragment>
                {beforePart}
                <MuiLink
                    css={css`
                        font-size: 11px;
                    `}
                    onClick={showSignLanguageChooser}
                >
                    {linkPart}
                </MuiLink>
                {afterPart}
            </React.Fragment>
        );
    }

    const noActivitiesTooltip = useL10n(
        "This book does not have any interactive activities.",
        "PublishTab.Feature.Activities.None"
    );
    const hasActivitiesTooltip = useL10n(
        "This book has interactive activities.",
        "PublishTab.Feature.Activities.Present"
    );

    const enterpriseRequiredTooltip = useL10n(
        "This is disabled because publishing interactive activities is a Bloom Enterprise feature.",
        "PublishTab.Feature.Activities.RequiresEnterprise"
    );

    const activitiesTooltip = hasActivities
        ? enterpriseAvailable
            ? hasActivitiesTooltip
            : enterpriseRequiredTooltip
        : noActivitiesTooltip;

    const checkTheActivityBox = hasActivities && enterpriseAvailable;

    const noComicTooltip = useL10n(
        "This is disabled because this book does not have any overlay elements that qualify as “comic-like”, such as speech bubbles.",
        "PublishTab.Feature.Comic.None"
    );
    const hasComicTooltip = useL10n(
        "Select this if you want to list this book as a Comic Book. This only affects how the book is categorized, it does not affect the content of the book.",
        "PublishTab.Feature.Comic.Possible"
    );
    const comicTooltip = comicEnabled ? hasComicTooltip : noComicTooltip;

    const noMotionTooltip = useL10n(
        "This is disabled because this book does not have any pages with motion enabled.",
        "PublishTab.Feature.Motion.None"
    );
    const hasMotionTooltip = useL10n(
        "Select this if you want to show motion when the book is read in landscape mode.",
        "PublishTab.Feature.Motion.Possible"
    );
    const motionTooltip = motionEnabled ? hasMotionTooltip : noMotionTooltip;

    const noVisionTooltip = useL10n(
        "This is disabled because this book does not have any image descriptions in the primary language.",
        "PublishTab.Feature.Vision.None"
    );
    const hasVisionTooltip = useL10n(
        "Select this if you want to list this book as accessible to the visually impaired in the primary language.  This only affects how the book is categorized, it does not affect the content of the book.",
        "PublishTab.Feature.Vision.Possible"
    );
    const visionTooltip = visuallyImpairedEnabled
        ? hasVisionTooltip
        : noVisionTooltip;

    return (
        <SettingsGroup
            label={useL10n("Features", "PublishTab.Android.Features")}
        >
            <FormGroup>
                <BloomTooltip key={"tb-tooltip"} tip={talkingBookTooltip}>
                    <BloomCheckbox
                        label="Talking Book"
                        l10nKey="PublishTab.TalkingBook"
                        icon={<TalkingBookIcon />}
                        iconScale={0.9}
                        disabled={!isTalkingBook}
                        checked={isTalkingBook}
                        onCheckChanged={() => {}}
                        hideBox={true}
                    />
                </BloomTooltip>
                <BloomTooltip
                    key={"sl-tooltip"}
                    id={"sl-tooltip"}
                    tip={slTooltip}
                >
                    <ApiCheckbox
                        // Changing the key each time signLanguageEnabled changes ensures that the checkbox rerenders with the latest value from the server.
                        // Otherwise, if the user sets the sign language, we will not get the checkbox checked by default.
                        key={`signLanguageFeature-${signLanguageEnabled}`}
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
                                    align-self: center;
                                `}
                                color={kBloomBlue}
                            />
                        }
                        // tooltip={slTooltip}
                        disabled={!signLanguageEnabled}
                    />
                </BloomTooltip>
                <BloomTooltip key={"activity-tooltip"} tip={activitiesTooltip}>
                    <BloomCheckbox
                        label="Activity"
                        l10nKey="PublishTab.Activity"
                        icon={<ActivityIcon />}
                        iconScale={0.9}
                        disabled={!checkTheActivityBox}
                        checked={checkTheActivityBox}
                        onCheckChanged={() => {}}
                        hideBox={true}
                    />
                </BloomTooltip>
                <BloomTooltip key={"comic-tooltip"} tip={comicTooltip}>
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
                                    align-self: center;
                                `}
                                color={kBloomBlue}
                            />
                        }
                        disabled={!comicEnabled}
                    />
                </BloomTooltip>
                <BloomTooltip key={"motion-tooltip"} tip={motionTooltip}>
                    <ApiCheckbox
                        english="Motion Book"
                        l10nKey="PublishTab.Android.MotionBookMode"
                        // tslint:disable-next-line:max-line-length
                        l10nComment="Motion Books are Talking Books in which the picture fills the screen, then pans and zooms while you hear the voice recording. This happens only if you turn the book sideways."
                        apiEndpoint="publish/motionBookMode"
                        icon={<MotionIcon color={kBloomBlue} />}
                        onChange={props.onChange}
                        disabled={!motionEnabled}
                    />
                </BloomTooltip>
                <BloomTooltip key={"visual-tooltip"} tip={visionTooltip}>
                    <ApiCheckbox
                        english="Accessible to the Visually Impaired in %0"
                        l10nKey="PublishTab.AccessibleVisually"
                        l10nParam0={l1Name}
                        apiEndpoint="publish/visuallyImpaired"
                        icon={
                            <VisuallyImpairedIcon
                                css={css`
                                    align-self: start;
                                `}
                                color={kBloomBlue}
                            />
                        }
                        iconScale={0.9}
                        disabled={!visuallyImpairedEnabled}
                    />
                </BloomTooltip>
            </FormGroup>
        </SettingsGroup>
    );
};
