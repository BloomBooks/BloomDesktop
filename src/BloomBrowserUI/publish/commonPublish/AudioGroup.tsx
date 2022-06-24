import * as React from "react";
import { FormGroup } from "@material-ui/core";
import { SettingsGroup } from "./PublishScreenBaseComponents";

export const AudioGroup: React.FunctionComponent = () => (
    <SettingsGroup label="Audio todo localize">
        <FormGroup>
            {/* <FeatureSwitch label="Narration" />
            <FeatureSwitch label="Background Music" /> */}
        </FormGroup>
    </SettingsGroup>
);
