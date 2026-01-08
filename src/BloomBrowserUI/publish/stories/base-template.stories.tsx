import { Meta, StoryObj } from "@storybook/react-vite";
import PublishScreenTemplate from "../commonPublish/PublishScreenTemplate";
import PublishScreenBanner from "../commonPublish/PublishScreenBanner";
import { Button, Typography } from "@mui/material";
import { loremIpsum } from "lorem-ipsum";

const someButton = (
    <div>
        <Button variant="contained" color="primary">
            Some control 1
        </Button>
        <Button variant="contained" color="primary">
            Some control 2
        </Button>
    </div>
);

const optionHeader = (
    <div>
        <Typography variant="h6">Options Panel here:</Typography>
        <p>{loremIpsum({ count: 5 })}</p>
    </div>
);

const testMainPanelContents = (
    <div>
        <Typography variant="h2">Main Panel Contents here:</Typography>
        <p>{loremIpsum({ count: 5 })}</p>
        <p>{loremIpsum({ count: 7 })}</p>
        <p>{loremIpsum({ count: 3 })}</p>
        <p>{loremIpsum({ count: 4 })}</p>
    </div>
);

const meta: Meta = {
    title: "Publish/BaseTemplate",
};

export default meta;
type Story = StoryObj;

export const PublishScreenTemplateStory: Story = {
    name: "PublishScreenTemplate",
    render: () => (
        <PublishScreenTemplate
            bannerTitleEnglish="Publish as Audio or Video"
            bannerTitleL10nId="PublishTab.RecordVideo.BannerTitle"
            bannerDescriptionL10nId="PublishTab.RecordVideo.BannerDescription"
            bannerDescriptionMarkdown="Create video files that you can upload to sites like Facebook and [YouTube](https://www.youtube.com). You can also make videos to share with people who use inexpensive “feature phones” and even audio-only files for listening."
            bannerRightSideControls={someButton}
            optionsPanelContents={optionHeader}
        >
            {testMainPanelContents}
        </PublishScreenTemplate>
    ),
};

export const PublishScreenBannerEPub: Story = {
    name: "PublishScreenBanner/ePUB",
    render: () => (
        <PublishScreenBanner
            titleEnglish="Publish as ePUB"
            titleL10nId="PublishTab.Epub.BannerTitle"
        >
            {someButton}
        </PublishScreenBanner>
    ),
};
