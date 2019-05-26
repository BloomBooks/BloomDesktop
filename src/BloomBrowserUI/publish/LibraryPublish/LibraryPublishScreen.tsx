import * as React from "react";
import ReactDOM = require("react-dom");
import { Link } from "@material-ui/core";

import {
    BasePublishScreen,
    PreviewPanel,
    PublishPanel,
    HelpGroup,
    SettingsPanel
} from "../commonPublish/BasePublishScreen";

import { LibraryPublishSteps } from "./LibraryPublishSteps";
import { PublishFeaturesGroup } from "../commonPublish/PublishFeaturesGroup";
import { LanguageGroup } from "../commonPublish/LanguageGroup";
import { AudioGroup } from "../commonPublish/AudioGroup";
import { LibraryPreview } from "./LibraryPreview";
import { ThemeProvider } from "@material-ui/styles";
import theme from "../../bloomMaterialUITheme";

export const LibraryPublishScreen = () => {
    return (
        <BasePublishScreen className="LibraryPublishScreen">
            <PreviewPanel>
                <LibraryPreview />
            </PreviewPanel>
            <PublishPanel>
                <LibraryPublishSteps />
            </PublishPanel>
            <SettingsPanel>
                <PublishFeaturesGroup />
                <LanguageGroup />
                <AudioGroup />
                <HelpGroup>
                    <Link variant="body2">About BloomLibrary.org</Link>
                </HelpGroup>
            </SettingsPanel>
        </BasePublishScreen>
    );
};
// a bit goofy... currently the html loads everything in publishUIBundlejs. So all the publish screens
// get any not-in-a-class code called, including ours. But it only makes sense to get wired up
// if that html has the root page we need.
if (document.getElementById("LibraryPublishScreen")) {
    ReactDOM.render(
        <ThemeProvider theme={theme}>
            <LibraryPublishScreen />
        </ThemeProvider>,
        document.getElementById("LibraryPublishScreen")
    );
}
