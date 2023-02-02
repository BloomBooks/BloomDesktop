import * as React from "react";
import { useState } from "react";
import Button from "@mui/material/Button";
import "./LibraryPublish.less";
import Stepper from "@mui/material/Stepper";
import Step from "@mui/material/Step";
import StepLabel from "@mui/material/StepLabel";
import StepContent from "@mui/material/StepContent";
import { FormControlLabel, Checkbox, TextField } from "@mui/material";

// This is a set of radio buttons and image that goes with each choice, plus a button to start off the sharing/saving
export const LibraryPublishSteps: React.FunctionComponent = () => {
    return (
        <>
            <Stepper orientation="vertical">
                <Step active={true}>
                    <StepLabel>Give your book a summary</StepLabel>
                    <StepContent>
                        <TextField
                            // needed by aria for a11y
                            id="book summary"
                            className={"summary"}
                            // value={this.state.name}
                            //onChange={this.handleChange('name')}
                            label="Summary"
                            margin="normal"
                            variant="outlined"
                            InputLabelProps={{
                                shrink: true
                            }}
                            multiline
                            rows="2"
                            aria-label="Book summary"
                        />
                    </StepContent>
                </Step>
                <Step active={true}>
                    <StepLabel>
                        Check over all the metadata and options in the preview
                        above
                    </StepLabel>
                    {/* without the StepContent, the line doesn't start in the right spot */}
                    <StepContent>{""}</StepContent>
                </Step>
                <Step active={true}>
                    <StepLabel>
                        <Button color="primary">LOG IN...</Button>
                        &nbsp;or&nbsp;
                        <Button color="primary">Sign Up...</Button>
                    </StepLabel>
                </Step>
                <Step active={true}>
                    <StepLabel>
                        <AgreementCheckbox />
                    </StepLabel>
                </Step>
                <Step active={true}>
                    <StepLabel>
                        <Button variant="contained" color="primary">
                            Upload
                        </Button>
                    </StepLabel>
                </Step>
            </Stepper>
        </>
    );
};
// Todo:
// Progress of upload
// Real preview data
// warn if copyright not set
// Choose languages to upload
// Really hook up login/signup
// Disable Upload until all done
// Upload button should say "to sandbox" if appropriate
// Features

const AgreementCheckbox: React.FunctionComponent = props => {
    const [checked, setChecked] = useState(true);
    return (
        <FormControlLabel
            control={
                <Checkbox
                    checked={checked}
                    onChange={(e, newState) => setChecked(newState)}
                />
            }
            label="I agree with the Bloom Library Terms of Use and grant the rights it describes."
        />
    );
};
