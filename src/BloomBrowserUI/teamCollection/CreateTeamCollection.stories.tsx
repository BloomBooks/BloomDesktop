import { CreateTeamCollectionDialog } from "./CreateTeamCollection";

export default {
    title: "Collection/Team Collection/CreateTeamCollection",
};

export const _CreateTeamCollectionDialog = () => (
    <CreateTeamCollectionDialog
        collectionName="Sample Collection"
        onFolderChosen={() => {}}
        onClose={() => {}}
        defaultRepoFolder=""
    />
);

_CreateTeamCollectionDialog.story = {
    name: "CreateTeamCollection Dialog",
};

export const CreateTeamCollectionDialogShowingPath = () => (
    <CreateTeamCollectionDialog
        collectionName="Sample Collection"
        onFolderChosen={() => {}}
        onClose={() => {}}
        defaultRepoFolder="z:\Enim aute dolore ex voluptate commodo\"
    />
);

CreateTeamCollectionDialogShowingPath.story = {
    name: "CreateTeamCollection Dialog showing path",
};

export const CreateTeamCollectionDialogShowingError = () => (
    <CreateTeamCollectionDialog
        collectionName="Sample Collection"
        onFolderChosen={() => {}}
        onClose={() => {}}
        defaultRepoFolder=""
        errorForTesting="Commodo veniam laboris ut ut ea laboris Lorem Lorem laborum enim minim velit."
    />
);

CreateTeamCollectionDialogShowingError.story = {
    name: "CreateTeamCollection Dialog showing error",
};
