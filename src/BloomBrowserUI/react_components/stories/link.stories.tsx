import { Link } from "../link";

import { Meta, StoryObj } from "@storybook/react-vite";

const meta: Meta = {
    title: "Localizable Widgets/Link",
};

export default meta;
type Story = StoryObj;

export const EnabledLink: Story = {
    name: "enabled",
    render: () => <Link l10nKey="bogus">link text</Link>,
};

// Disabled link not included as per original comment:
// Setting the disabled prop actually only adds a disabled class which has no effect on its own.
// So I'm not including the story for now. Else it is just confusing.
