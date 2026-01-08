import { JoinTeamCollectionDialog } from "./JoinTeamCollectionDialog";
import { normalDialogEnvironmentForStorybook } from "../react_components/BloomDialog/BloomDialogPlumbing";

export default {
    title: "Team Collection components/JoinTeamCollection",
};

export const NewCollection = () => (
    <div id="reactRoot" className="JoinTeamCollection">
        <JoinTeamCollectionDialog
            collectionName="foobar"
            existingCollection={false}
            isAlreadyTcCollection={false}
            isCurrentCollection={false}
            isSameCollection={false}
            existingCollectionFolder=""
            conflictingCollection=""
            dialogEnvironment={normalDialogEnvironmentForStorybook}
        />
    </div>
);

NewCollection.story = {
    name: "new collection",
};

export const ExistingCollection = () => (
    <div id="reactRoot" className="JoinTeamCollection">
        <JoinTeamCollectionDialog
            collectionName="foobar"
            existingCollection={true}
            isAlreadyTcCollection={false}
            isCurrentCollection={false}
            isSameCollection={false}
            existingCollectionFolder="somewhere"
            conflictingCollection=""
            dialogEnvironment={normalDialogEnvironmentForStorybook}
        />
    </div>
);

ExistingCollection.story = {
    name: "existing collection",
};

export const ExistingTcCollectionSameLocationAndGuid = () => (
    <div id="reactRoot" className="JoinTeamCollection">
        <JoinTeamCollectionDialog
            collectionName="foobar"
            existingCollection={true}
            isAlreadyTcCollection={true}
            isCurrentCollection={true}
            isSameCollection={true}
            existingCollectionFolder="some good place"
            conflictingCollection=""
            dialogEnvironment={normalDialogEnvironmentForStorybook}
        />
    </div>
);

ExistingTcCollectionSameLocationAndGuid.story = {
    name: "existing TC collection, same location and guid",
};

export const ExistingTcCollectionDifferentLocationSameGuid = () => (
    <div id="reactRoot" className="JoinTeamCollection">
        <JoinTeamCollectionDialog
            collectionName="foobar"
            existingCollection={true}
            isAlreadyTcCollection={true}
            isCurrentCollection={false}
            isSameCollection={true}
            existingCollectionFolder="some good place"
            conflictingCollection="some bad place"
            dialogEnvironment={normalDialogEnvironmentForStorybook}
        />
    </div>
);

ExistingTcCollectionDifferentLocationSameGuid.story = {
    name: "existing TC collection, different location same guid",
};

export const ExistingTcCollectionDifferentLocationAndGuid = () => (
    <div id="reactRoot" className="JoinTeamCollection">
        <JoinTeamCollectionDialog
            collectionName="foobar"
            existingCollection={true}
            isAlreadyTcCollection={true}
            isCurrentCollection={false}
            isSameCollection={false}
            existingCollectionFolder="some good place"
            conflictingCollection="some bad place"
            dialogEnvironment={normalDialogEnvironmentForStorybook}
        />
    </div>
);

ExistingTcCollectionDifferentLocationAndGuid.story = {
    name: "existing TC collection, different location and guid",
};

export const ExistingCollectionBareFrame = () => (
    <div id="reactRoot" className="JoinTeamCollection">
        <JoinTeamCollectionDialog
            collectionName="foobar"
            existingCollection={true}
            isAlreadyTcCollection={false}
            isCurrentCollection={false}
            isSameCollection={false}
            existingCollectionFolder="somewhere"
            conflictingCollection=""
            dialogEnvironment={{
                dialogFrameProvidedExternally: true,
                initiallyOpen: true,
            }}
        />
    </div>
);

ExistingCollectionBareFrame.story = {
    name: "existing collection, bare frame",
};
