import * as React from "react";
import FormGroup from "@material-ui/core/FormGroup";
import { SettingsGroup } from "./BasePublishScreen";

export const LanguageGroup: React.FunctionComponent = () => (
    <SettingsGroup label="Languages todo l10n">
        <FormGroup>
            {/* <FeatureSwitch label="English" />
            <FeatureSwitch label="Español" /> */}
        </FormGroup>
    </SettingsGroup>
);
