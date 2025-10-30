import { Meta, StoryObj } from "@storybook/react-vite";
import { AccessibilityCheckScreen } from "../accessibilityCheck/accessibilityCheckScreen";
import "../storiesApiMocks";

const meta: Meta<typeof AccessibilityCheckScreen> = {
    title: "Publish/Accessibility",
    component: AccessibilityCheckScreen,
};

export default meta;
type Story = StoryObj<typeof AccessibilityCheckScreen>;

export const AccessibilityCheckScreenStory: Story = {
    name: "AccessibilityCheckScreen",
    render: () => <AccessibilityCheckScreen />,
};
