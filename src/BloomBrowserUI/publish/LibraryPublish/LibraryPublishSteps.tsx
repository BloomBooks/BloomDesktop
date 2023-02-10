/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { useEffect, useState } from "react";
import { TextField, Step, StepLabel, StepContent } from "@mui/material";
import { BloomStepper } from "../../react_components/BloomStepper";
import { Div, P } from "../../react_components/l10nComponents";
import BloomButton from "../../react_components/bloomButton";
import { PWithLink } from "../../react_components/pWithLink";
import { ProgressBox } from "../../react_components/Progress/progressBox";
import { MuiCheckbox } from "../../react_components/muiCheckBox";

export const LibraryPublishSteps: React.FunctionComponent = () => {
    const [summary, setSummary] = useState<string>(
        // TODO use real data
        "His favourite blue cap that was bought at the fair gets stuck in the tree. How will the boy be consoled?"
    );
    const [isReadyForUpload, setIsReadyForUpload] = useState<boolean>(false);
    const [isUploadComplete, setIsUploadComplete] = useState<boolean>(false);

    return (
        <BloomStepper orientation="vertical">
            <Step active={true} completed={isReadyForUpload}>
                <StepLabel>Confirm Metadata</StepLabel>
                <StepContent>
                    <LabelWrapper labelText="Title">
                        {/* TODO use real data */}
                        my book title
                    </LabelWrapper>
                    <TextField
                        // needed by aria for a11y
                        id="book summary"
                        value={summary}
                        onChange={e => setSummary(e.target.value)}
                        label="Summary"
                        margin="normal"
                        variant="outlined"
                        InputLabelProps={{
                            shrink: true
                        }}
                        multiline
                        rows="2"
                        aria-label="Book summary"
                        fullWidth
                        css={css`
                            margin-left: -15px; // Align the label with the read-only data labels. Determined experimentally.
                            margin-top: 24px;

                            // This is messy. MUI doesn't seem to let you easily (and correctly) change the label size.
                            // You're supposed to be able to set a style on InputLabelProps and set fontSize, but then
                            // the border around the textbox partially goes through it.
                            // The way that break in the border is implemented is a "legend" which obscures the border.
                            // The legend has the same text as the label. So we have to make the text the same size.
                            // The original transform is translate(14px, -9px) scale(1). In order to make "larger" match,
                            // we unscale it here -- scale(1), and as a result we have to increase the scale of the legend.
                            .MuiInputLabel-root {
                                color: inherit;
                                font-weight: 500;
                                font-size: larger;
                                transform: translate(14px, -9px) scale(1);
                                &.Mui-focused {
                                    color: inherit;
                                }
                            }
                            legend {
                                font-weight: 500;
                                font-size: larger;
                                transform: scale(1.5);
                            }
                        `}
                    />
                    <LabelWrapper labelText="Copyright">
                        {/* TODO use real data */}
                        Copyright 2019 Napil National Language Preservation
                        Institute
                    </LabelWrapper>
                    <LabelWrapper labelText="Usage/License">
                        {/* TODO use real data */}
                        Creative Commons CC-BY-NC-SA
                    </LabelWrapper>
                </StepContent>
            </Step>
            <Step active={true} completed={isReadyForUpload}>
                <StepLabel>Agreements</StepLabel>
                <StepContent>
                    <Agreements onReadyChange={setIsReadyForUpload} />
                </StepContent>
            </Step>
            <Step
                active={isReadyForUpload}
                expanded={true}
                disabled={!isReadyForUpload}
                completed={isUploadComplete}
            >
                <StepLabel>Upload</StepLabel>
                <StepContent>
                    <MuiCheckbox
                        label={
                            <React.Fragment>
                                <img src="/bloom/publish/LibraryPublish/DRAFT-Stamp.svg" />
                                Show this book only to reviewers with whom I
                                share the URL of this book.
                            </React.Fragment>
                        }
                        checked={false}
                        onCheckChanged={newValue => {
                            //TODO
                        }}
                        disabled={!isReadyForUpload}
                    />
                    <div
                        css={css`
                            display: flex;
                            justify-content: space-between;
                            margin-top: 8px;
                        `}
                    >
                        <BloomButton
                            enabled={isReadyForUpload}
                            l10nKey={"TODO"}
                            temporarilyDisableI18nWarning={true}
                            onClick={() =>
                                // TODO: start upload
                                // when upload is complete call:
                                setIsUploadComplete(true)
                            }
                        >
                            Upload
                        </BloomButton>
                        <BloomButton
                            variant="text"
                            enabled={isReadyForUpload}
                            l10nKey={"TODO"}
                            temporarilyDisableI18nWarning={true}
                        >
                            Sign in or sign up to Bloomlibrary.org
                        </BloomButton>
                    </div>
                    <div
                        css={css`
                            margin-top: 16px;
                        `}
                    >
                        <Div
                            l10nKey={"TODO"}
                            temporarilyDisableI18nWarning={true}
                        >
                            Upload Progress
                        </Div>
                        <ProgressBox
                            css={css`
                                height: 200px;
                            `}
                        ></ProgressBox>
                    </div>
                </StepContent>
            </Step>
            <Step
                active={isUploadComplete}
                expanded={isUploadComplete}
                disabled={!isUploadComplete}
            >
                <StepLabel>Test out your Book</StepLabel>
                <StepContent>
                    <PWithLink
                        l10nKey={"TODO"}
                        href={"TODO"}
                        temporarilyDisableI18nWarning={true}
                    >
                        Here is [your new page] on Bloom Library. We will soon
                        process your book into various formats and add them to
                        this page. Check back in about 10 minutes. If we
                        encounter any problems, that page will tell you about
                        them.
                    </PWithLink>
                    <P l10nKey={"TODO"} temporarilyDisableI18nWarning={true}>
                        If you make changes to this book, you can return here to
                        upload it again. Your new version will just replace the
                        existing one.
                    </P>
                </StepContent>
            </Step>
        </BloomStepper>
    );
};

// JH's original TODO list:
// (some of these apply to the Settings)
// Progress of upload
// Real preview data
// warn if copyright not set
// Choose languages to upload
// Really hook up login/signup
// Disable Upload until all done
// Upload button should say "to sandbox" if appropriate
// Features

const Agreements: React.FunctionComponent<{
    onReadyChange: (v: boolean) => void;
}> = props => {
    const totalCheckboxes = 3;
    const [numChecked, setNumChecked] = useState<number>(0);
    useEffect(() => {
        props.onReadyChange(numChecked === totalCheckboxes);
    }, [numChecked]);
    function handleChange(isChecked: boolean) {
        setNumChecked(prevNumChecked =>
            isChecked ? prevNumChecked + 1 : prevNumChecked - 1
        );
    }
    return (
        <React.Fragment>
            <AgreementCheckbox
                label={
                    <PWithLink
                        href={"TODO"}
                        l10nKey={"TODO"}
                        temporarilyDisableI18nWarning={true}
                    >
                        I have permission to publish all the text and images in
                        this book. [Learn More]
                    </PWithLink>
                }
                onChange={checked => handleChange(checked)}
            />
            <AgreementCheckbox
                label={
                    <P l10nKey={"TODO"} temporarilyDisableI18nWarning={true}>
                        The book gives credit to the the author, translator, and
                        illustrator(s).
                    </P>
                }
                onChange={checked => handleChange(checked)}
            />
            <AgreementCheckbox
                label={
                    <PWithLink
                        href={"https://bloomlibrary.org/terms"}
                        l10nKey={"TODO"}
                        temporarilyDisableI18nWarning={true}
                    >
                        I agree to the [Bloom Library Terms of Use] and grant
                        the rights it describes.
                    </PWithLink>
                }
                onChange={checked => handleChange(checked)}
            />
        </React.Fragment>
    );
};

const AgreementCheckbox: React.FunctionComponent<{
    label: string | React.ReactNode;
    onChange: (v: boolean) => void;
}> = props => {
    const [isChecked, setIsChecked] = useState(false);
    function handleCheckChanged(isChecked: boolean) {
        setIsChecked(isChecked);
        props.onChange(isChecked);
    }
    return (
        <div>
            <MuiCheckbox
                label={props.label}
                checked={isChecked}
                onCheckChanged={newState => {
                    handleCheckChanged(!!newState);
                }}
            ></MuiCheckbox>
        </div>
    );
};

const LabelWrapper: React.FunctionComponent<{
    labelText: string;
}> = props => {
    return (
        <div
            css={css`
                font-size: larger;
                margin-top: 15px;
            `}
        >
            <div
                css={css`
                    font-weight: 500;
                `}
            >
                {props.labelText}
            </div>
            {props.children}
        </div>
    );
};
