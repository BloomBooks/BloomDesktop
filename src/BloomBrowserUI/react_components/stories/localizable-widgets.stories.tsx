import { Expandable as Expandable1 } from "../expandable";
import BloomButton from "../bloomButton";
import { BloomSplitButton } from "../bloomSplitButton";
import { ImportIcon } from "../../bookEdit/toolbox/talkingBook/TalkingBookToolboxIcons";
import DeleteIcon from "@mui/icons-material/Delete";

import { Meta, StoryObj } from "@storybook/react";

const meta: Meta = {
    title: "Localizable Widgets"
};

export default meta;
type Story = StoryObj;

export const Expandable: Story = {
    name: "Expandable",
    render: () => (
        <Expandable1
            l10nKey="bogus"
            expandedHeight="30px"
            headingText="I am so advanced"
        >
            Look at this!
        </Expandable1>
    )
};

export const BloomButtonStory: Story = {
    name: "BloomButton",
    render: () => (
        <div>
            <BloomButton
                l10nKey="bogus"
                l10nComment="hello"
                enabled={true}
                hasText={true}
            >
                Look at this!
            </BloomButton>
            <br /> <br />
            <BloomButton
                l10nKey="bogus"
                l10nComment="hello"
                enabled={true}
                hasText={true}
                variant="text"
            >
                Variant = text
            </BloomButton>
            <br /> <br />
            <BloomButton
                l10nKey="bogus"
                l10nComment="hello"
                enabled={true}
                hasText={true}
                variant="outlined"
            >
                Variant = outlined
            </BloomButton>
            <br /> <br />
            <BloomButton
                iconBeforeText={<DeleteIcon />}
                l10nKey="bogus"
                l10nComment="hello"
                enabled={true}
                hasText={true}
            >
                Material icon
            </BloomButton>
            <br /> <br />
            <BloomButton
                iconBeforeText={<ImportIcon />}
                l10nKey="bogus"
                l10nComment="hello"
                enabled={true}
                hasText={true}
                variant="outlined"
            >
                Custom icon
            </BloomButton>
            <br /> <br />
            <BloomButton
                iconBeforeText={<ImportIcon />}
                l10nKey="bogus"
                l10nComment="hello"
                enabled={true}
                hasText={true}
                size="small"
                variant="outlined"
            >
                Small
            </BloomButton>
        </div>
    )
};

export const BloomSplitButtonStory: Story = {
    name: "BloomSplitButton",
    render: () => (
        <div>
            <BloomSplitButton
                options={[
                    {
                        english: "Option 1",
                        l10nId: "already-localized",
                        featureName: "foobar",

                        onClick: () => {
                            alert("Option 1 clicked");
                        }
                    },
                    {
                        english: "Option 2",
                        l10nId: "already-localized",
                        onClick: () => {
                            alert("Option 2 clicked");
                        }
                    }
                ]}
            ></BloomSplitButton>
        </div>
    )
};
