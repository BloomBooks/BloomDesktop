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

export enum ProblemKind {
    User = "User",
    NonFatal = "NonFatal",
    Fatal = "Fatal"
}

enum Mode {
    gather,
    submitting,
    submitted,
    showPrivacyDetails
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

    const readyToSubmit = (email: string, userInput: string): boolean => {
        return isValidEmail(email) && userInput.trim().length !== 0;
    };

    const whatWereYouDoingAttentionClass = useDrawAttention(
        submitAttempts,
        () => whatDoing.trim().length > 0
    );

    const submitButton = useRef(null);
    // Haven't got to work yet, see comment on the declaration of this function, below
    useCtrlEnterToSubmit(() => {
        if (submitButton && submitButton.current) {
            (submitButton.current as any).click();
        }
    });

    const translateHowMuch = (): string => {
        return howMuch === 0
            ? "0 (First time)"
            : howMuch === 2
            ? "2 (It keeps happening)"
            : "1 (It happens sometimes)";
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
                    userInput: `${whatDoing}\n\nHow much: ${translateHowMuch()}`,
                    includeBook,
                    includeScreenshot
                },
                result => {
                    console.log(JSON.stringify(result.data));
                    setIssueLink(result.data.issueLink);
                    setMode(Mode.submitted);
                }
            );
        }
    };

    return (
        <ThemeProvider theme={theme}>
            <Dialog
                className="progress-dialog"
                open={true}
                // the behavior of fullWidth/maxWidth is very strange
                //fullWidth={true}
                maxWidth={"md"}
                fullScreen={true}
                onClose={() => BloomApi.post("dialog/close")}
            >
                <DialogTitle>
                    {kindParams[props.kind.toString()].title}
                </DialogTitle>
                <DialogContent className="content">
                    {(() => {
                        switch (mode) {
                            case Mode.submitting:
                                return <Typography>Submitting...</Typography>;
                            case Mode.submitted:
                                return (
                                    <>
                                        <Typography>Done</Typography>
                                        {issueLink !== "" && (
                                            <Typography>
                                                <br />
                                                This issue can be viewed here:{" "}
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
                                            Please help us reproduce this
                                            problem on our computers.
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
                                                    label="What were you doing?"
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
                                                    label="Include book '{0}'"
                                                    l10nKey="ReportProblemDialog.IncludeBookButton"
                                                    l10nParam0={bookName}
                                                    checked={includeBook}
                                                    onCheckChanged={v =>
                                                        setIncludeBook(
                                                            v as boolean
                                                        )
                                                    }
                                                />
                                                <MuiCheckbox
                                                    label="Include this screenshot:"
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
                <DialogActions>
                    {mode === Mode.submitted &&
                        props.kind !== ProblemKind.Fatal && (
                            <BloomButton
                                enabled={true}
                                l10nKey="ReportProblemDialog.Close"
                                hasText={true}
                                onClick={() => {
                                    BloomApi.post("dialog/close");
                                }}
                            >
                                Close
                            </BloomButton>
                        )}
                    {mode === Mode.submitted &&
                        props.kind === ProblemKind.Fatal && (
                            <BloomButton
                                enabled={true}
                                l10nKey="ReportProblemDialog.Quit"
                                hasText={true}
                                onClick={() => {
                                    BloomApi.post("dialog/close");
                                }}
                            >
                                Quit
                            </BloomButton>
                        )}
                    {mode === Mode.gather && (
                        <BloomButton
                            enabled={readyToSubmit(email, whatDoing)}
                            l10nKey="ReportProblemDialog.SubmitButton"
                            hasText={true}
                            onClick={() => {
                                AttemptSubmit();
                            }}
                        >
                            Submit
                        </BloomButton>
                    )}
                    {mode === Mode.gather && props.kind === ProblemKind.User && (
                        <BloomButton
                            enabled={true}
                            l10nKey="Common.Cancel"
                            hasText={true}
                            variant="outlined"
                            onClick={() => {
                                BloomApi.post("dialog/close");
                            }}
                            ref={submitButton}
                        >
                            Cancel
                        </BloomButton>
                    )}
                </DialogActions>
            </Dialog>
        </ThemeProvider>
    );
};

/* haven't got this to work yet; when the callback is called, `email` and other values are empty*/
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
