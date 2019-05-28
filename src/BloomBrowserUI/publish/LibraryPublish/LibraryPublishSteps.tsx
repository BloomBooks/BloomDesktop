import * as React from "react";
import { useState } from "react";
import Button from "@material-ui/core/Button";
import "./LibraryPublish.less";
import Stepper from "@material-ui/core/Stepper";
import Step from "@material-ui/core/Step";
import StepLabel from "@material-ui/core/StepLabel";
import StepContent from "@material-ui/core/StepContent";
import { FormControlLabel, Checkbox, TextField } from "@material-ui/core";

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
