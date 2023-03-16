import * as React from "react";
import { FormGroup } from "@mui/material";
import { SettingsGroup } from "./PublishScreenBaseComponents";
import { useL10n } from "../../react_components/l10nHooks";
import { ApiCheckbox } from "../../react_components/ApiCheckbox";
import { useApiBoolean } from "../../utils/bloomApi";

export const AudioGroup: React.FunctionComponent = () => {
    const label = useL10n("Include Audio", "PublishTab.Upload.IncludeAudio");
    const [narrationEnabled] = useApiBoolean(
        "libraryPublish/narrationEnabled",
        false
    );
    const [musicEnabled] = useApiBoolean("libraryPublish/musicEnabled", false);
    return (
        <SettingsGroup label={label}>
            <FormGroup>
                <ApiCheckbox
                    english="Narration"
                    l10nKey="PublishTab.Upload.Narration"
                    disabled={!narrationEnabled}
                    apiEndpoint="libraryPublish/narration"
                />
                <ApiCheckbox
                    english="Background Music"
                    l10nKey="PublishTab.Upload.BackgroundMusic"
                    disabled={!musicEnabled}
                    apiEndpoint="libraryPublish/music"
                />
            </FormGroup>
        </SettingsGroup>
    );
};
