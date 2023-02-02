import * as React from "react";
import FormGroup from "@mui/material/FormGroup";
import { SettingsGroup } from "./PublishScreenBaseComponents";

export const LanguageGroup: React.FunctionComponent = () => (
    <SettingsGroup label="Languages todo l10n">
        <FormGroup>
            {/* <FeatureSwitch label="English" />
            <FeatureSwitch label="Español" /> */}
        </FormGroup>
    </SettingsGroup>
);
