import * as React from "react";
import ReactDOM = require("react-dom");
import { Link } from "@mui/material";

import {
    PreviewPanel,
    PublishPanel,
    HelpGroup,
    SettingsPanel
} from "../commonPublish/PublishScreenBaseComponents";

import { LibraryPublishSteps } from "./LibraryPublishSteps";
import { PublishFeaturesGroup } from "../ReaderPublish/PublishFeaturesGroup";
import { LanguageGroup } from "../commonPublish/LanguageGroup";
import { AudioGroup } from "../commonPublish/AudioGroup";
import { LibraryPreview } from "./LibraryPreview";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { lightTheme } from "../../bloomMaterialUITheme";
import PublishScreenTemplate from "../commonPublish/PublishScreenTemplate";

export const LibraryPublishScreen = () => {
    const mainPanel = (
        // <>
        //     <PreviewPanel>
        //         <LibraryPreview />
        //     </PreviewPanel>
        <PublishPanel>
            <LibraryPublishSteps />
        </PublishPanel>
        // </>
    );

    const settingsPanel = (
        <SettingsPanel>
            <PublishFeaturesGroup />
            <LanguageGroup />
            <AudioGroup />
            <HelpGroup>
                <Link variant="body2">About BloomLibrary.org</Link>
            </HelpGroup>
        </SettingsPanel>
    );
    return (
        <PublishScreenTemplate
            bannerTitleEnglish="Publish to Web"
            bannerTitleL10nId="TODO"
            bannerDescriptionMarkdown="Let speakers find your books in Bloom Reader and on BloomLibrary.org"
            bannerDescriptionL10nId="TODO"
            optionsPanelContents={settingsPanel}
        >
            {mainPanel}
        </PublishScreenTemplate>
    );
};
// a bit goofy... currently the html loads everything in publishUIBundlejs. So all the publish screens
// get any not-in-a-class code called, including ours. But it only makes sense to get wired up
// if that html has the root page we need.
if (document.getElementById("LibraryPublishScreen")) {
    ReactDOM.render(
        <StyledEngineProvider injectFirst>
            <ThemeProvider theme={lightTheme}>
                <LibraryPublishScreen />
            </ThemeProvider>
        </StyledEngineProvider>,
        document.getElementById("LibraryPublishScreen")
    );
}
