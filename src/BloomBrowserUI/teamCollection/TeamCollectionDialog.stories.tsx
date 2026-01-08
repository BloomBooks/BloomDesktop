import { TeamCollectionDialogLauncher } from "./TeamCollectionDialog";
import { StorybookDialogWrapper } from "../react_components/BloomDialog/BloomDialogPlumbing";

export default {
    title: "Team Collection components/TeamCollectionDialog",
};

export const WithReloadButton = () => (
    <StorybookDialogWrapper
        id="TeamCollectionDialog"
        params={{ showReloadButton: true }}
    >
        <TeamCollectionDialogLauncher />
    </StorybookDialogWrapper>
);

WithReloadButton.story = {
    name: "With reload button",
};

export const WithoutReloadButton = () => (
    <StorybookDialogWrapper
        id="TeamCollectionDialog"
        params={{ showReloadButton: false }}
    >
        <TeamCollectionDialogLauncher />
    </StorybookDialogWrapper>
);

WithoutReloadButton.story = {
    name: "Without reload button",
};
