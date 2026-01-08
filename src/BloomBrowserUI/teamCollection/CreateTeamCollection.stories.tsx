import { CreateTeamCollectionDialog } from "./CreateTeamCollection";
import { normalDialogEnvironmentForStorybook } from "../react_components/BloomDialog/BloomDialogPlumbing";

export default {
    title: "Team Collection components/CreateTeamCollection",
};

export const _CreateTeamCollectionDialog = () => (
    <CreateTeamCollectionDialog
        dialogEnvironment={normalDialogEnvironmentForStorybook}
    />
);

_CreateTeamCollectionDialog.story = {
    name: "CreateTeamCollection Dialog",
};

export const CreateTeamCollectionDialogShowingPath = () => (
    <CreateTeamCollectionDialog
        dialogEnvironment={normalDialogEnvironmentForStorybook}
        defaultRepoFolder="z:\Enim aute dolore ex voluptate commodo\"
    />
);

CreateTeamCollectionDialogShowingPath.story = {
    name: "CreateTeamCollection Dialog showing path",
};

export const CreateTeamCollectionDialogShowingError = () => (
    <CreateTeamCollectionDialog
        dialogEnvironment={normalDialogEnvironmentForStorybook}
        errorForTesting="Commodo veniam laboris ut ut ea laboris Lorem Lorem laborum enim minim velit."
    />
);

CreateTeamCollectionDialogShowingError.story = {
    name: "CreateTeamCollection Dialog showing error",
};
