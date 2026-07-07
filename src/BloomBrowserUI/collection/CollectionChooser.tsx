import { css } from "@emotion/react";
import { useState } from "react";
import { post, useApiData } from "../utils/bloomApi";
import { CollectionCardList } from "./CollectionCardList";
import { ICollectionInfo } from "./CollectionCard";
import { MyCloudCollectionsSection } from "./MyCloudCollectionsSection";
import {
    ICloudCollectionSummary,
    useIsCloudTeamCollectionsExperimentalFeatureEnabled,
    useMyCloudCollections,
    useSharingLoginState,
} from "../teamCollection/sharingApi";
import { JoinCloudCollectionDialog } from "../teamCollection/JoinCloudCollectionDialog";

export const CollectionChooser: React.FunctionComponent<{
    collections?: ICollectionInfo[];
}> = (props) => {
    let collections = props.collections || [];
    const collectionsFromApi = useApiData<ICollectionInfo[]>(
        "collections/getMostRecentlyUsedCollections",
        [],
    );
    if (!props.collections?.length) collections = collectionsFromApi;

    // "Get my Team Collections": the cloud collections the signed-in user is approved for,
    // with a pull-down-to-join action. See Design/CloudTeamCollections/tasks/07-ui-setup.md.
    // The whole section (and its queries) exists only when the cloud-team-collections
    // experimental feature is on; everyone else gets the pre-cloud chooser unchanged.
    const cloudFeatureEnabled =
        useIsCloudTeamCollectionsExperimentalFeatureEnabled();
    const loginState = useSharingLoginState();
    const { collections: cloudCollections, loading: cloudCollectionsLoading } =
        useMyCloudCollections(cloudFeatureEnabled && loginState.signedIn);

    // The collection a pull-down was requested for, or undefined when the join dialog should
    // be hidden. Embedded directly here (not opened as a separate WinForms dialog -- see
    // JoinCloudCollectionDialog.tsx's own comment) so it shares CollectionChooser's already-
    // fetched `loginState`/`cloudCollections`.
    const [joinTarget, setJoinTarget] = useState<
        ICloudCollectionSummary | undefined
    >(undefined);

    return (
        <div
            css={css`
                display: flex;
                flex-grow: 1;
                gap: 16px;
                min-height: 0;
            `}
        >
            <CollectionCardList
                collections={collections}
                css={css`
                    flex-grow: 1;
                    min-width: 0;
                    overflow-y: auto;
                `}
            />
            {cloudFeatureEnabled && (
                <MyCloudCollectionsSection
                    loginState={loginState}
                    collections={cloudCollections}
                    loading={cloudCollectionsLoading}
                    onSignInClick={() => post("sharing/showSignIn")}
                    onPullDown={(collectionId) => {
                        const target = cloudCollections.find(
                            (c) => c.collectionId === collectionId,
                        );
                        // Should always be found: the button that triggers this is only ever
                        // rendered for a row from this same cloudCollections list.
                        if (target) setJoinTarget(target);
                    }}
                />
            )}
            {joinTarget && (
                <JoinCloudCollectionDialog
                    collectionId={joinTarget.collectionId}
                    collectionName={joinTarget.name}
                    signedIn={loginState.signedIn}
                    // Listed in "Get my Team Collections" at all only because the signed-in
                    // user is approved for it (collections/mine), so this is known-true here.
                    isApproved={true}
                    // No endpoint exposes the six local-vs-remote matching flags below ahead of
                    // a real pull-down attempt (CloudJoinFlow resolves them server-side, inside
                    // collections/pullDown itself -- see JoinCloudCollectionDialog's own
                    // handleJoinClick comment). Defaulting to "no local conflict" shows the
                    // ordinary CreateNewCollection copy; if pulling down actually hits one of
                    // the conflict scenarios, the dialog surfaces the server's real error
                    // message instead of silently mismatching its copy to the wrong state.
                    existingCollection={false}
                    isAlreadyTcCollection={false}
                    isSameCollection={false}
                    isCurrentCollection={false}
                    existingCollectionFolder=""
                    conflictingCollection=""
                    // Rendered directly in the tree (not wrapped by a WinForms dialog frame),
                    // and always mounted "open" -- the surrounding `joinTarget &&` guard is
                    // what actually shows/hides it.
                    dialogEnvironment={{
                        dialogFrameProvidedExternally: false,
                        initiallyOpen: true,
                    }}
                    onClose={() => setJoinTarget(undefined)}
                />
            )}
        </div>
    );
};
