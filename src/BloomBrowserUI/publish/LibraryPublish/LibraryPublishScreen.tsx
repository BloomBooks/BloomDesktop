/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import { Typography } from "@mui/material";
import { Link } from "../../react_components/link";
import {
    PublishPanel,
    HelpGroup,
    SettingsPanel
} from "../commonPublish/PublishScreenBaseComponents";

import { LibraryPublishSteps } from "./LibraryPublishSteps";
import { PublishFeaturesGroup } from "../commonPublish/PublishFeaturesGroup";
import PublishScreenTemplate from "../commonPublish/PublishScreenTemplate";
import { PublishLanguagesGroup } from "../commonPublish/PublishLanguagesGroup";
import { useState } from "react";
import { PublishVisibilityGroup } from "../commonPublish/PublishVisibilityGroup";
import HelpLink from "../../react_components/helpLink";
import { PublishTopic } from "../commonPublish/PublishTopic";

export const kWebSocketContext = "libraryPublish";

export const LibraryPublishScreen = () => {
    const mainPanel = (
        <PublishPanel>
            <LibraryPublishSteps />
        </PublishPanel>
    );

    const [generation, setGeneration] = useState(0);

    const settingsPanel = (
        <SettingsPanel>
            <PublishLanguagesGroup
                onChange={() => {
                    // Forces features group to re-evaluate whether this will be a talking book.
                    setGeneration(old => old + 1);
                }}
            />
            <PublishFeaturesGroup generation={generation} />
            <PublishVisibilityGroup />
            <PublishTopic />

            {/* push everything below this to the bottom */}
            <div
                css={css`
                    margin-top: auto;
                `}
            />
            <HelpGroup>
                <Link
                    href="https://docs.bloomlibrary.org/why-share-on-blorg"
                    l10nKey="PublishTab.Upload.WhyShareOnBlorg"
                >
                    Why you should publish to the Web
                </Link>
                <HelpLink
                    helpId="Tasks/Publish_tasks/Publish to Web.htm"
                    l10nKey="PublishTab.Upload.Help"
                >
                    Documentation about this screen
                </HelpLink>
                <HelpLink
                    helpId="Tasks/Publish_tasks/Publish_tasks_overview.htm"
                    l10nKey="PublishTab.TasksOverview"
                >
                    Publish tab tasks overview
                </HelpLink>
                <Link
                    href="https://bloomLibrary.org/about"
                    l10nKey={"PublishTab.Upload.AboutBlorg"}
                >
                    About BloomLibrary.org
                </Link>
            </HelpGroup>
        </SettingsPanel>
    );
    return (
        // I'm not actually sure why we want this Typography wrapper, but PublishAudioVideo has it,
        // and this is needed to keep the look consistent between the two screens.
        <Typography
            component={"div"}
            css={css`
                height: 100%;
            `}
        >
            <PublishScreenTemplate
                bannerTitleEnglish="Publish to Web"
                bannerTitleL10nId="PublishTab.Upload.BannerTitle"
                bannerDescriptionMarkdown="Let speakers find your books in [Bloom Reader](https://bloomlibrary.org/page/create/bloom-reader) and on [BloomLibrary.org](https://bloomlibrary.org/)."
                bannerDescriptionL10nId="PublishTab.Upload.BannerDescription"
                optionsPanelContents={settingsPanel}
            >
                {mainPanel}
            </PublishScreenTemplate>
        </Typography>
    );
};
