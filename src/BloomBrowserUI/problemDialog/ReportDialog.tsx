import * as React from "react";
import {
    Dialog,
    DialogActions,
    DialogContent,
    DialogTitle,
    Link,
    TextField,
    Typography
} from "@mui/material";
import { post, postJson, useApiStringState } from "../utils/bloomApi";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import "./ProblemDialog.less";
import BloomButton from "../react_components/bloomButton";
import { BloomCheckbox } from "../react_components/BloomCheckBox";
import { useState, useEffect, useRef } from "react";
import { HowMuchGroup } from "./HowMuchGroup";
import { PrivacyNotice } from "./PrivacyNotice";
import { makeTheme, kindParams } from "./theme";
import { EmailField, isValidEmail } from "./EmailField";
import { useDrawAttention } from "../react_components/UseDrawAttention";
import { PrivacyScreen } from "./PrivacyScreen";
import { useL10n } from "../react_components/l10nHooks";
import { ProblemKind } from "./ProblemDialog";

enum Mode {
    gather,
    submitting,
    submitted,
    showPrivacyDetails,
    submissionFailed
}

export const ReportDialog: React.FunctionComponent<{
    kind: ProblemKind;
}> = props => {
    const [mode, setMode] = useState(Mode.gather);
    const [includeBook, setIncludeBook] = useState(true);
    const [includeScreenshot, setIncludeScreenshot] = useState(true);

    // Precondition: The returned string from BloomServer must already encode any special characters
    // which are not meant to be treated as HTML code.
    const [reportHeadingHtml] = useApiStringState(
        "problemReport/reportHeadingHtml",
        ""
    );
    const [email, setEmail] = useApiStringState(
        "problemReport/emailAddress",
        ""
    );
    const [submitAttempts, setSubmitAttempts] = useState(0);
    const theme = makeTheme(props.kind);
    const [whatDoing, setWhatDoing] = useState("");
    const [bookName] = useApiStringState("problemReport/bookName", "??");

    // When submitted, this will contain the url of the YouTrack issue.
    const [issueLink, setIssueLink] = useState("");
    const [howMuch, setHowMuch] = useState(1); // 0, 1 or 2

    const [zippedReportPath, setZippedReportPath] = useState("");

    useEffect(() => {
        setIncludeBook(bookName !== "??");
    }, [bookName]);

    const readyToSubmit = (email: string, userInput: string): boolean => {
        return isValidEmail(email) && userInput.trim().length !== 0;
    };

    const whatWereYouDoingAttentionClass = useDrawAttention(
        submitAttempts,
        () => whatDoing.trim().length > 0
    );

    const submitButton = useRef(null);
    useCtrlEnterToSubmit(() => {
        if (submitButton && submitButton.current) {
            (submitButton.current as any).onClick();
        }
    });

    const stringifyHowMuch = (): string => {
        switch (howMuch) {
            case 0:
                return "0 (First time)";
            case 1:
                return "1 (It happens sometimes)";
            case 2:
                return "2 (It keeps happening)";
            default:
                return `unknown value ${howMuch} for 'how much'`;
        }
    };

    const AttemptSubmit = () => {
        if (!readyToSubmit(email, whatDoing)) {
            setSubmitAttempts(submitAttempts + 1);
        } else {
            setMode(Mode.submitting);
            postJson(
                "problemReport/submit",
                {
                    kind: props.kind,
                    email,
                    userInput: `${whatDoing}\n\nHow much: ${stringifyHowMuch()}`,
                    includeBook,
                    includeScreenshot
                },
                result => {
                    if (result.data.failed) {
                        // result.data.zippedReportPath may or may not be set; we deal with that elsewhere.
                        setZippedReportPath(result.data.zippedReportPath);
                        setMode(Mode.submissionFailed);
                    } else {
                        setIssueLink(result.data.issueLink);
                        setMode(Mode.submitted);
                    }
                }
            );
        }
    };

    // I (gjm) started off putting the calls to useL10n() right in the JSX,
    // but I found that I was getting a strange error from React about calling various
    // hooks in a different order. I figured it might have had something to do with
    // switching mode quickly between submitting and submitted. Pulling the calls to
    // useL10n() out here seems to solve the problem.
    const englishTitle = kindParams[props.kind.toString()].title;
    const titleKey = kindParams[props.kind.toString()].l10nKey;
    const localizedDlgTitle = useL10n(englishTitle, titleKey);
    const localizedWhatDoingLabel = useL10n(
        "What were you doing?",
        "ReportProblemDialog.WhatDoing",
        "This is the label for the text field where the user enters what they were doing at the time of the problem."
    );
    const localizedPleaseHelpUs = useL10n(
        "Please help us reproduce this problem on our computers.",
        "ReportProblemDialog.PleaseHelpUs"
    );
    const localizedIssueLinkLabel = useL10n(
        "This issue can be viewed here:",
        "ReportProblemDialog.IssueLink",
        "This label is displayed before a link to the issue after it is created."
    );
    const localizedSubmittingMsg = useL10n(
        "Submitting to server...",
        "ReportProblemDialog.Submitting",
        "This is shown while Bloom is sending the problem report to our server."
    );
    const localizedFailureWithZipMsg = useL10n(
        "Bloom was not able to submit your report directly to our server. Please retry or email {0} to {1}.",
        "ReportProblemDialog.CouldNotSendToServer",
        undefined,
        zippedReportPath,
        "issues@bloomlibrary.org"
    );
    // This general failure message without a zip path should be extremely rare, and it isn't worth localizing.
    const generalFailureMsg =
        "Bloom was not able to submit your report to our server. Please retry or email issues@bloomlibrary.org.";
    const localizedDone = useL10n("Done", "Common.Done");

    // Gives us a Cancel, Close, or Quit button.
    const getEndingButton = (): JSX.Element | null => {
        let l10nKey: string;
        let buttonLabel: string;
        if (mode === Mode.gather && props.kind === ProblemKind.User) {
            l10nKey = "Common.Cancel";
            buttonLabel = "Cancel";
        } else {
            // Note: At one point, we only included this button if mode was not Submitted nor SubmissionFailed.
            // Now, we include it all the time. Since we have Sentry reporting too, there's less need to
            // try to funnel people towards submitting.
            buttonLabel = props.kind === ProblemKind.Fatal ? "Quit" : "Close";
            l10nKey =
                props.kind === ProblemKind.Fatal
                    ? "ReportProblemDialog.Quit"
                    : "Common.Close";
        }

        return (
            <BloomButton
                enabled={true}
                l10nKey={l10nKey}
                hasText={true}
                variant="outlined"
                onClick={() => {
                    post("common/closeReactDialog");
                }}
            >
                {buttonLabel}
            </BloomButton>
        );
    };

    // This will show the appropriate dialog action buttons for each case.
    // Cancel is only shown for a user-initiated dialog, but users will probably discover
    // eventually that they can just hit 'Esc' to get out of sending an error message
    // for the 'Fatal' and 'NonFatal' versions.
    const getDialogActionButtons = (): JSX.Element => {
        return (
            <>
                {mode === Mode.gather && (
                    <BloomButton
                        enabled={true}
                        l10nKey="ReportProblemDialog.SubmitButton"
                        hasText={true}
                        onClick={() => {
                            AttemptSubmit();
                        }}
                        ref={submitButton}
                    >
                        Submit
                    </BloomButton>
                )}
                {mode !== Mode.showPrivacyDetails && getEndingButton()}
            </>
        );
    };

    return (
        <StyledEngineProvider injectFirst>
            <ThemeProvider theme={theme}>
                <Dialog
                    className="problem-dialog"
                    open={true}
                    // the behavior of fullWidth/maxWidth is very strange
                    //fullWidth={true}
                    maxWidth={"md"}
                    fullScreen={true}
                    onClose={() => post("common/closeReactDialog")}
                >
                    <DialogTitle className="dialog-title">
                        <Typography variant="h6">
                            {localizedDlgTitle}
                        </Typography>
                        {/* We moved the X up to the winforms dialog so that it is draggable
                             <Close
                            className="close-in-title"
                            onClick={() => post("common/closeReactDialog")}
                        /> */}
                    </DialogTitle>
                    <DialogContent className="content">
                        {(() => {
                            switch (mode) {
                                case Mode.submitting:
                                    return (
                                        <Typography>
                                            {localizedSubmittingMsg}
                                        </Typography>
                                    );
                                case Mode.submitted:
                                    return (
                                        <>
                                            {issueLink !== "" && (
                                                <Typography>
                                                    {localizedDone}
                                                    <br />
                                                    {
                                                        localizedIssueLinkLabel
                                                    }{" "}
                                                    <Link
                                                        underline="hover"
                                                        href={issueLink}
                                                    >
                                                        {issueLink}
                                                    </Link>
                                                </Typography>
                                            )}
                                        </>
                                    );
                                case Mode.submissionFailed:
                                    return (
                                        <Typography className="allowSelect">
                                            {zippedReportPath
                                                ? localizedFailureWithZipMsg
                                                : generalFailureMsg}
                                        </Typography>
                                    );
                                case Mode.showPrivacyDetails:
                                    return (
                                        <PrivacyScreen
                                            includeBook={includeBook}
                                            email={email}
                                            userInput={whatDoing}
                                            onBack={() => setMode(Mode.gather)}
                                        />
                                    );
                                case Mode.gather:
                                    return (
                                        <>
                                            <Typography
                                                className="report-heading allowSelect"
                                                dangerouslySetInnerHTML={{
                                                    __html: reportHeadingHtml
                                                }}
                                            ></Typography>
                                            <Typography id="please_help_us">
                                                {localizedPleaseHelpUs}
                                            </Typography>
                                            <div id="row2">
                                                <div className="column1">
                                                    <TextField
                                                        // can't use id for css because that goes down to a child element
                                                        className={
                                                            "what_were_you_doing " +
                                                            whatWereYouDoingAttentionClass
                                                        }
                                                        autoFocus={true}
                                                        variant="outlined"
                                                        label={
                                                            localizedWhatDoingLabel
                                                        }
                                                        rows="3"
                                                        InputLabelProps={{
                                                            shrink: true
                                                        }}
                                                        multiline={true}
                                                        aria-label="What were you doing?"
                                                        onChange={event => {
                                                            setWhatDoing(
                                                                event.target
                                                                    .value
                                                            );
                                                        }}
                                                        error={
                                                            submitAttempts >
                                                                0 &&
                                                            whatDoing.trim()
                                                                .length === 0
                                                        }
                                                        value={whatDoing}
                                                    />
                                                    <HowMuchGroup
                                                        onHowMuchChange={value =>
                                                            setHowMuch(value)
                                                        }
                                                    />

                                                    <EmailField
                                                        email={email}
                                                        onChange={v =>
                                                            setEmail(v)
                                                        }
                                                        submitAttempts={
                                                            submitAttempts
                                                        }
                                                    />
                                                </div>
                                                <div className="column2">
                                                    <BloomCheckbox
                                                        className="includeBook"
                                                        label="Include Book '{0}'"
                                                        l10nKey="ReportProblemDialog.IncludeBookButton"
                                                        l10nParam0={bookName}
                                                        checked={includeBook}
                                                        disabled={
                                                            bookName === "??"
                                                        }
                                                        onCheckChanged={v =>
                                                            setIncludeBook(
                                                                v as boolean
                                                            )
                                                        }
                                                    />
                                                    <BloomCheckbox
                                                        label="Include this screenshot"
                                                        l10nKey="ReportProblemDialog.IncludeScreenshotButton"
                                                        checked={
                                                            includeScreenshot
                                                        }
                                                        onCheckChanged={v =>
                                                            setIncludeScreenshot(
                                                                v as boolean
                                                            )
                                                        }
                                                    />
                                                    <img
                                                        src={
                                                            "/bloom/api/problemReport/screenshot"
                                                        }
                                                    />
                                                    <PrivacyNotice
                                                        onLearnMore={() =>
                                                            setMode(
                                                                Mode.showPrivacyDetails
                                                            )
                                                        }
                                                    />
                                                </div>
                                            </div>
                                        </>
                                    );
                            }
                        })()}
                    </DialogContent>
                    <DialogActions>{getDialogActionButtons()}</DialogActions>
                </Dialog>
            </ThemeProvider>
        </StyledEngineProvider>
    );
};

function useCtrlEnterToSubmit(callback) {
    useEffect(() => {
        const handler = event => {
            if (event.ctrlKey && event.key === "Enter") {
                callback();
            }
        };

        window.addEventListener("keydown", handler);
        return () => {
            window.removeEventListener("keydown", handler);
        };
    }, []);
}
