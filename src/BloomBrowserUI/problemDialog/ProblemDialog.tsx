import * as React from "react";
import {
    Dialog,
    DialogActions,
    DialogContent,
    DialogTitle,
    Link,
    TextField,
    Typography
} from "@material-ui/core";
import { BloomApi } from "../utils/bloomApi";
import { ThemeProvider } from "@material-ui/styles";
import "./ProblemDialog.less";
import BloomButton from "../react_components/bloomButton";
import { MuiCheckbox } from "../react_components/muiCheckBox";
import { useState, useEffect, useRef } from "react";
import { HowMuchGroup } from "./HowMuchGroup";
import { PrivacyNotice } from "./PrivacyNotice";
import { makeTheme, kindParams } from "./theme";
import { EmailField, isValidEmail } from "./EmailField";
import { useDrawAttention } from "./UseDrawAttention";
import ReactDOM = require("react-dom");
import { PrivacyScreen } from "./PrivacyScreen";
import { useL10n } from "../react_components/l10nHooks";
import Close from "@material-ui/icons/Close";

export enum ProblemKind {
    User = "User",
    NonFatal = "NonFatal",
    Fatal = "Fatal"
}

enum Mode {
    gather,
    submitting,
    submitted,
    showPrivacyDetails,
    submissionFailed
}

export const ProblemDialog: React.FunctionComponent<{
    kind: ProblemKind;
}> = props => {
    const [mode, setMode] = useState(Mode.gather);
    const [includeBook, setIncludeBook] = useState(true);
    const [includeScreenshot, setIncludeScreenshot] = useState(true);
    const [email, setEmail] = BloomApi.useApiString(
        "problemReport/emailAddress",
        ""
    );
    const [submitAttempts, setSubmitAttempts] = useState(0);
    const theme = makeTheme(props.kind);
    const [whatDoing, setWhatDoing] = useState("");
    const [bookName] = BloomApi.useApiString("problemReport/bookName", "??");

    // When submitted, this will contain the url of the YouTrack issue.
    const [issueLink, setIssueLink] = useState("");
    const [howMuch, setHowMuch] = useState(1); // 0, 1 or 2

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
            BloomApi.postJson(
                "problemReport/submit",
                {
                    kind: props.kind,
                    email,
                    userInput: `${whatDoing}\n\nHow much: ${stringifyHowMuch()}`,
                    includeBook,
                    includeScreenshot
                },
                result => {
                    console.log(JSON.stringify(result.data));
                    const failureResponseString = "failed:";
                    const link = result.data.issueLink;
                    if (link.startsWith(failureResponseString)) {
                        setIssueLink(
                            link.substring(failureResponseString.length)
                        );
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
    const localizedFailureMsg = useL10n(
        "Bloom was not able to submit your report directly to our server. Please retry or email {0} to {1}.",
        "ReportProblemDialog.CouldNotSendToServer",
        undefined,
        issueLink,
        "issues@bloomlibrary.org"
    );
    const localizedDone = useL10n("Done", "Common.Done");

    const needCancelButton = (): boolean => {
        return mode === Mode.gather && props.kind === ProblemKind.User;
    };

    // Assuming we've tried to submit a report (no matter the result),
    // this will give us either a Close or Quit button.
    const getEndingButtonIfAppropriate = (): JSX.Element | null => {
        if (mode !== Mode.submitted && mode !== Mode.submissionFailed) {
            return null;
        }
        const keyword = props.kind === ProblemKind.Fatal ? "Quit" : "Close";
        const l10nKey = `ReportProblemDialog.${keyword}`;
        return (
            <BloomButton
                enabled={true}
                l10nKey={l10nKey}
                hasText={true}
                onClick={() => {
                    BloomApi.post("dialog/close");
                }}
            >
                {keyword}
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
                {getEndingButtonIfAppropriate()}
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
                {needCancelButton() && (
                    <BloomButton
                        enabled={true}
                        l10nKey="Common.Cancel"
                        hasText={true}
                        variant="outlined"
                        onClick={() => {
                            BloomApi.post("dialog/close");
                        }}
                    >
                        Cancel
                    </BloomButton>
                )}
            </>
        );
    };

    return (
        <ThemeProvider theme={theme}>
            <Dialog
                className="problem-dialog"
                open={true}
                // the behavior of fullWidth/maxWidth is very strange
                //fullWidth={true}
                maxWidth={"md"}
                fullScreen={true}
                onClose={() => BloomApi.post("dialog/close")}
            >
                {/* The whole disableTypography and Typography thing gets around Material-ui putting the
                    Close icon inside of the title's Typography element, where we don't have control over its CSS. */}
                <DialogTitle className="dialog-title" disableTypography={true}>
                    <Typography variant="h6">{localizedDlgTitle}</Typography>
                    <Close
                        className="close-in-title"
                        onClick={() => BloomApi.post("dialog/close")}
                    />
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
                                                {localizedIssueLinkLabel}{" "}
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
                                    <>
                                        {issueLink !== "" && (
                                            <Typography>
                                                {localizedFailureMsg}
                                            </Typography>
                                        )}
                                    </>
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
                                                            event.target.value
                                                        );
                                                    }}
                                                    error={
                                                        submitAttempts > 0 &&
                                                        whatDoing.trim()
                                                            .length == 0
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
                                                    onChange={v => setEmail(v)}
                                                    submitAttempts={
                                                        submitAttempts
                                                    }
                                                />
                                            </div>
                                            <div className="column2">
                                                <MuiCheckbox
                                                    label="Include Book '{0}'"
                                                    l10nKey="ReportProblemDialog.IncludeBookButton"
                                                    l10nParam0={bookName}
                                                    checked={includeBook}
                                                    disabled={bookName === "??"}
                                                    onCheckChanged={v =>
                                                        setIncludeBook(
                                                            v as boolean
                                                        )
                                                    }
                                                />
                                                <MuiCheckbox
                                                    label="Include this screenshot"
                                                    l10nKey="ReportProblemDialog.IncludeScreenshotButton"
                                                    checked={includeScreenshot}
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

// allow plain 'ol javascript in the html to connect up react
(window as any).connectProblemDialog = (element: Element | null) => {
    const levelQuery = window.location.search;
    const kind = levelQuery.length > 1 ? levelQuery.substring(1) : "fatal"; // strip off initial "?"
    const kindProp =
        kind === "fatal"
            ? ProblemKind.Fatal
            : kind === "nonfatal"
            ? ProblemKind.NonFatal
            : ProblemKind.User;
    ReactDOM.render(<ProblemDialog kind={kindProp} />, element);
};
