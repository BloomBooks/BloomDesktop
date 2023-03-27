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
                    icon={
                        <svg
                            width="19"
                            height="18"
                            viewBox="0 0 19 18"
                            fill={kBloomBlue}
                            xmlns="http://www.w3.org/2000/svg"
                        >
                            <path d="M0.847656 5.83079V11.8308H4.84766L9.84766 16.8308V0.830791L4.84766 5.83079H0.847656ZM14.3477 8.83079C14.3477 7.06079 13.3277 5.54079 11.8477 4.80079V12.8508C13.3277 12.1208 14.3477 10.6008 14.3477 8.83079ZM11.8477 0.060791V2.12079C14.7377 2.98079 16.8477 5.66079 16.8477 8.83079C16.8477 12.0008 14.7377 14.6808 11.8477 15.5408V17.6008C15.8577 16.6908 18.8477 13.1108 18.8477 8.83079C18.8477 4.55079 15.8577 0.970791 11.8477 0.060791Z" />
                        </svg>
                    }
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
                        // All the icons in this file are taken from the contents of the corresponding
                        // svg files in BloomLibrary's assets folder.
                        // (I could not get them to work, with Icon compoments that have color control, in the way BL does it.
                        // Online searches indicate that approach is only possible in a Create React App, though they
                        // probably really only mean that something obscure and tricky must be done in Webpack to make it work.)
                        // Some look different from the icons we use for the corresponding feature in our Toolbox; if we decide
                        // to use these there, we should pull them into files or components somehow.
                        // Todo: I think JohnH wants tooltips describing what they mean in more detail and explaining
                        // why some are disabled, but these have not been drafted yet.
                        <svg
                            css={css`
                                height: 20px;
                                align-self: center;
                                // I would prefer if this happened automatically as a result of the check box
                                // being disabled. But that currently just modifies the css color. I don't
                                // think there is a way to make the SVG be displayed using the current
                                // CSS foreground color.
                                ${signLanguageEnabled ? "" : "opacity: 38%;"}
                            `}
                            width="23"
                            height="25"
                            viewBox="0 0 23 25"
                            fill={kBloomBlue}
                            xmlns="http://www.w3.org/2000/svg"
                        >
                            <path
                                fillRule="evenodd"
                                clipRule="evenodd"
                                d="M6.17044 18.7878C6.16358 13.7813 6.16016 11.2784 6.16016 11.2784C6.24094 10.1488 6.71392 9.66214 7.5786 9.81833C8.11082 10.0886 8.37522 10.6938 8.3713 11.6329C8.56568 14.3865 8.66312 15.7634 8.66312 15.7634C8.84183 16.3568 9.30061 16.3636 10.0399 15.7839C14.6292 10.7501 16.9236 8.23293 16.9236 8.23293C18.0066 7.56753 18.696 8.22216 18.3631 9.19259C15.5262 12.7247 14.1077 14.4908 14.1077 14.4908C14.0084 14.9447 14.2243 15.0696 14.7541 14.8664C18.342 11.7093 20.136 10.1312 20.136 10.1312C21.223 9.64502 21.8149 10.4367 21.1373 11.4454C17.9386 14.602 16.3395 16.1805 16.3395 16.1805C16.1235 16.7054 16.3253 16.9487 16.9446 16.9105C20.1429 14.6578 21.742 13.5312 21.742 13.5312C22.1714 13.4842 22.4637 13.561 22.6184 13.7608C22.8892 14.1236 22.896 14.4575 22.639 14.7621C19.4824 17.2513 17.9038 18.496 17.9038 18.496C17.6096 19.0022 17.7486 19.2559 18.321 19.2573C20.7965 17.9358 22.0343 17.2753 22.0343 17.2753C22.4774 17.1598 22.7487 17.2499 22.8476 17.5466C22.9328 17.8898 22.78 18.2027 22.3888 18.4852C17.3329 22.056 14.1729 24.0346 12.9082 24.4199C10.4263 24.8469 8.45847 24.1443 7.00474 22.3131C6.53764 21.2819 6.25954 20.1068 6.17043 18.7878L6.17044 18.7878Z"
                            />
                            <path
                                fillRule="evenodd"
                                clipRule="evenodd"
                                d="M0.956755 11.9043C2.53971 13.4808 4.00663 14.8298 5.35799 15.951C5.35799 15.951 5.35799 14.7064 5.35799 12.2171C2.91035 10.854 1.68678 10.1729 1.68678 10.1729C0.651712 10.1186 0.408368 10.6959 0.956746 11.9043H0.956755Z"
                            />
                            <path
                                fillRule="evenodd"
                                clipRule="evenodd"
                                d="M7.42836 8.99393C6.37126 8.95231 6.13477 9.36947 6.13477 9.36947C6.13477 9.36947 6.13672 6.93261 6.15043 2.32965C6.54018 1.03117 8.03549 1.34649 8.17405 2.45451C8.63283 6.62662 8.86197 8.71241 8.86197 8.71241C9.34376 9.01255 9.64341 8.94938 9.76092 8.52341C9.70413 3.46214 9.67573 0.931792 9.67573 0.931792C10.0699 -0.327523 11.574 -0.00535025 11.8658 1.24466C11.8937 6.39013 11.9079 8.96264 11.9079 8.96264C12.188 9.09728 12.4592 9.04195 12.7212 8.79567C13.2774 4.77683 13.5555 2.76743 13.5555 2.76743C14.1387 1.70543 15.0396 2.2734 15.2452 3.10135C14.8976 7.23134 14.7238 9.29658 14.7238 9.29658C11.8174 12.3979 10.3642 13.948 10.3642 13.948C9.91468 14.4798 9.5597 14.1272 9.50878 13.8858C9.36972 11.5356 9.12834 10.2249 9.12834 10.2249C8.87569 9.46552 8.23233 8.94406 7.42836 8.99401L7.42836 8.99393Z"
                            />
                        </svg>
                    }
                    title={slTitle}
                    disabled={!signLanguageEnabled}
                />
                <TickableBox
                    english="Activity"
                    l10nKey="PublishTab.Activity"
                    icon={
                        <svg
                            css={css`
                                height: 20px;
                                width: 20px;
                            `}
                            width="14"
                            height="9"
                            viewBox="0 0 14 9"
                            fill={kBloomBlue}
                            xmlns="http://www.w3.org/2000/svg"
                        >
                            <path d="M13.4777 6.50077C13.4623 6.45478 13.4623 6.40878 13.4468 6.37812L12.5343 3.46513C12.3642 2.94385 12.0239 2.14661 11.529 1.47203C10.9567 0.690117 10.3226 0.260834 9.70397 0.260834H4.52277C3.90412 0.260834 3.27 0.674785 2.69775 1.47203C2.20283 2.14661 1.86258 2.94385 1.69245 3.46513L0.779938 6.37812C0.764472 6.42412 0.749005 6.47011 0.749005 6.50077C0.56341 7.26735 0.888202 8.03393 1.56872 8.44788C2.28016 8.87716 3.14627 8.81584 3.78039 8.29456L5.54354 6.8074C5.8374 6.5621 6.20859 6.43945 6.59525 6.45478H6.61072L7.1211 6.43945L7.63149 6.45478H7.64695C8.03361 6.42412 8.4048 6.5621 8.69866 6.8074L10.4618 8.29456C10.8175 8.58586 11.2351 8.73918 11.6682 8.73918C12.0084 8.73918 12.3487 8.64719 12.6735 8.44788C13.3385 8.03393 13.6633 7.26735 13.4777 6.50077ZM4.75476 4.73765C3.95052 4.73765 3.30094 4.09372 3.30094 3.29648C3.30094 2.49924 3.95052 1.85531 4.75476 1.85531C5.55901 1.85531 6.20859 2.49924 6.20859 3.29648C6.20859 4.09372 5.55901 4.73765 4.75476 4.73765ZM9.06985 1.50269C9.5493 1.50269 9.95143 1.90131 9.95143 2.37659C9.95143 2.85187 9.5493 3.25049 9.06985 3.25049C8.5904 3.25049 8.18827 2.85187 8.18827 2.37659C8.18827 1.90131 8.5904 1.50269 9.06985 1.50269ZM7.43043 4.07839C7.05924 4.07839 6.74991 3.77176 6.74991 3.4038C6.74991 3.03584 7.05924 2.72921 7.43043 2.72921C7.80162 2.72921 8.11094 3.03584 8.11094 3.4038C8.11094 3.77176 7.80162 4.07839 7.43043 4.07839ZM9.06985 5.41223C8.5904 5.41223 8.18827 5.01361 8.18827 4.53834C8.18827 4.06306 8.5904 3.66444 9.06985 3.66444C9.5493 3.66444 9.95143 4.06306 9.95143 4.53834C9.95143 5.01361 9.5493 5.41223 9.06985 5.41223ZM10.601 4.07839C10.2298 4.07839 9.92049 3.77176 9.92049 3.4038C9.92049 3.03584 10.2298 2.72921 10.601 2.72921C10.9722 2.72921 11.2815 3.03584 11.2815 3.4038C11.2815 3.77176 10.9722 4.07839 10.601 4.07839Z" />
                        </svg>
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
                        <svg
                            css={css`
                                height: 20px;
                                width: 20px;
                                align-self: center;
                                ${comicEnabled ? "" : "opacity: 38%;"}
                            `}
                            width="16"
                            height="13"
                            viewBox="0 0 16 13"
                            fill={kBloomBlue}
                            xmlns="http://www.w3.org/2000/svg"
                        >
                            <path d="M7.73323 12.0258L6.20999 9.07883L3.0655 10.6755L4.24901 8.28283L0.0741272 8.94245L3.368 6.4883L0.326077 4.88834L3.77957 4.81031L2.44424 1.60357L5.87449 3.28545L6.41285 3.05176e-05L8.03313 2.98084L9.92389 0.897621L10.3161 3.77683L14.7415 2.38429L11.5017 5.68629L15.6609 7.25682L11.4944 7.61508L13.5615 10.6687L9.07035 8.84801L7.73323 12.0258ZM6.50892 8.15181L7.66902 10.3963L8.69779 7.95123L11.7155 9.17457L10.262 7.02731L12.571 6.82884L10.2664 5.95855L12.3124 3.87322L9.742 4.68223L9.43933 2.46003L7.89041 4.1663L6.76851 2.10226L6.40586 4.31601L3.7877 3.0322L4.80639 5.47855L3.01007 5.51919L4.66074 6.38735L2.73439 7.82233L5.46081 7.39158L4.60727 9.11751L6.50892 8.15181Z" />
                        </svg>
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
                    icon={
                        <svg
                            // Different from the motion icon we use in the toolbox...if we use it more, we should pull
                            // it into its own component.
                            width="20"
                            height="13"
                            viewBox="0 0 20 13"
                            fill={kBloomBlue}
                            xmlns="http://www.w3.org/2000/svg"
                        >
                            <path d="M6.99754 3.44625H1.17791C0.994155 3.44625 0.845428 3.29753 0.845428 3.11398C0.845428 2.93043 0.994155 2.78149 1.17791 2.78149H6.99754C7.18108 2.78149 7.33002 2.93022 7.33002 3.11398C7.33002 3.29774 7.18108 3.44625 6.99754 3.44625Z" />
                            <path d="M6.99761 6.44678H3.01934C2.8358 6.44678 2.68686 6.29806 2.68686 6.1143C2.68686 5.93075 2.83559 5.78181 3.01934 5.78181H6.99761C7.18116 5.78181 7.3301 5.93054 7.3301 6.1143C7.32989 6.29806 7.18116 6.44678 6.99761 6.44678Z" />
                            <path d="M6.99748 9.4471H4.86056C4.67701 9.4471 4.52808 9.29838 4.52808 9.11462C4.52808 8.93086 4.6768 8.78214 4.86056 8.78214H6.99748C7.18103 8.78214 7.32996 8.93086 7.32996 9.11462C7.32996 9.29838 7.18103 9.4471 6.99748 9.4471Z" />
                            <path d="M14.211 12.0258C11.171 12.0258 8.69742 9.55241 8.69742 6.51223C8.69742 3.47204 11.1708 0.998657 14.211 0.998657C17.2512 0.998657 19.7246 3.47204 19.7246 6.51223C19.7246 9.55241 17.2512 12.0258 14.211 12.0258ZM14.211 1.92919C11.6841 1.92919 9.62816 3.98514 9.62816 6.51223C9.62816 9.03932 11.6841 11.0953 14.211 11.0953C16.7379 11.0953 18.7938 9.03932 18.7938 6.51223C18.7938 3.98514 16.7381 1.92919 14.211 1.92919Z" />
                        </svg>
                    }
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
                        <svg
                            css={css`
                                height: 20px;
                                width: 20px;
                                align-self: start;
                                ${visuallyImpairedEnabled
                                    ? ""
                                    : "opacity: 38%;"}
                            `}
                            width="28"
                            height="26"
                            viewBox="0 0 28 26"
                            fill={kBloomBlue}
                            xmlns="http://www.w3.org/2000/svg"
                        >
                            <path d="M27.2142 12.7128C27.2142 12.7128 22.9857 4.66654 13.7599 4.66654C13.5877 4.66654 13.4184 4.67021 13.2499 4.67571L12.4446 0.626465L11.2092 0.90745L11.9777 4.77039C4.00926 5.7013 0.470703 12.6539 0.470703 12.6539C0.470703 12.6539 1.20577 14.2032 2.77129 15.9652L2.12675 12.7268C2.11333 12.7018 2.10619 12.6884 2.10619 12.6884C2.10619 12.6884 2.36292 12.2257 2.89409 11.5492L4.02411 17.2302C4.48817 17.6532 5.00021 18.0689 5.56079 18.4626L3.94786 10.3544C4.26799 10.03 4.6321 9.69251 5.03904 9.35686L7.03121 19.3712C7.48071 19.6116 7.95533 19.8342 8.45395 20.0315L6.16364 8.52093C6.53745 8.2714 6.93754 8.03073 7.36562 7.80441L9.89382 20.5104C10.3276 20.6286 10.7771 20.7264 11.2432 20.8027L10.2808 15.9655C10.7154 16.5299 11.2651 16.9874 11.8911 17.3011L12.6188 20.9579C12.9203 20.9768 13.227 20.9884 13.5409 20.9884C13.6665 20.9884 13.7911 20.986 13.9156 20.9823L13.267 17.7247C13.4561 17.7498 13.648 17.7641 13.8436 17.7641C14.0846 17.7641 14.3214 17.7446 14.5518 17.7064L16.01 25.0352L17.2448 24.7542L16.4389 20.7019C23.6211 19.2356 27.2142 12.7128 27.2142 12.7128ZM9.39577 11.5183L8.54704 7.25191C8.94571 7.08912 9.3655 6.9413 9.80386 6.81211L10.3582 9.59905C9.91438 10.1461 9.58083 10.7993 9.39577 11.5183ZM11.4531 8.6098L11.0355 6.51188C11.4382 6.43339 11.8551 6.37078 12.2872 6.3271L12.6339 8.06952C12.2135 8.19107 11.8169 8.37371 11.4531 8.6098ZM13.8913 7.89818L13.564 6.25503C13.6326 6.25411 13.7008 6.25258 13.7702 6.25258C14.0718 6.25258 14.3665 6.26236 14.6563 6.27976C17.6166 6.78157 19.8804 9.52301 19.8804 12.8304C19.8804 15.5792 18.316 17.9352 16.0885 18.9409L15.7652 17.3142C17.3527 16.5351 18.4553 14.8208 18.4553 12.8301C18.4551 10.1228 16.4166 7.92597 13.8913 7.89818ZM19.3221 18.1468C20.4912 16.7294 21.2011 14.8697 21.2011 12.8301C21.2011 10.6958 20.4227 8.75732 19.1553 7.31727C23.5292 9.15711 25.579 12.7354 25.579 12.7354C25.579 12.7354 23.4929 16.2129 19.3221 18.1468Z" />
                        </svg>
                    }
                    title={visionTitle}
                    disabled={!visuallyImpairedEnabled}
                />
            </FormGroup>
        </SettingsGroup>
    );
};
