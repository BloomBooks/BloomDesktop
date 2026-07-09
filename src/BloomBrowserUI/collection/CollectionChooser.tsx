import { css } from "@emotion/react";
import { useEffect, useState } from "react";
import { get, useApiData } from "../utils/bloomApi";
import { CollectionCardList, IJoinCollectionInfo } from "./CollectionCardList";
import { ICollectionInfo } from "./CollectionCard";
import {
    useIsCloudTeamCollectionsExperimentalFeatureEnabled,
    useSharingLoginState,
} from "../teamCollection/sharingApi";
import { JoinCloudCollectionDialog } from "../teamCollection/JoinCloudCollectionDialog";

// Fetches collections/getJoinCards (dogfood batch 1, item 6): one entry per cloud Team
// Collection the signed-in user belongs to but has no local copy of yet -- the server does the
// local-copy matching (CollectionChooserApi.ComputeJoinCards), so this hook is a thin fetch, no
// different in shape from useApiData except that it must NOT query at all when shouldQuery is
// false (folder-only or signed-out users must never trigger a cloud call from the chooser).
function useJoinCards(shouldQuery: boolean): IJoinCollectionInfo[] {
    const [joinCards, setJoinCards] = useState<IJoinCollectionInfo[]>([]);
    useEffect(() => {
        if (!shouldQuery) {
            setJoinCards([]);
            return;
        }
        get("collections/getJoinCards", (result) => {
            setJoinCards((result.data as IJoinCollectionInfo[]) ?? []);
        });
    }, [shouldQuery]);
    return joinCards;
}

export const CollectionChooser: React.FunctionComponent<{
    collections?: ICollectionInfo[];
}> = (props) => {
    let collections = props.collections || [];
    const collectionsFromApi = useApiData<ICollectionInfo[]>(
        "collections/getMostRecentlyUsedCollections",
        [],
    );
    if (!props.collections?.length) collections = collectionsFromApi;

    // Join cards (replaces the old "Get my Team Collections" sidebar -- see
    // Design/CloudTeamCollections/orchestration/DOGFOOD-BATCH-1.md item 6): cards appended to
    // the main list for cloud collections the user belongs to but hasn't joined locally yet.
    // Gated on the cloud-team-collections experimental feature (as the old sidebar was) and on
    // being signed in, so folder-only and signed-out users never trigger a cloud call here.
    const cloudFeatureEnabled =
        useIsCloudTeamCollectionsExperimentalFeatureEnabled();
    const loginState = useSharingLoginState();
    const joinCards = useJoinCards(cloudFeatureEnabled && loginState.signedIn);

    // The join card clicked, or undefined when the join dialog should be hidden. Embedded
    // directly here (not opened as a separate WinForms dialog -- see JoinCloudCollectionDialog's
    // own comment) so it shares CollectionChooser's already-fetched `loginState`.
    const [joinTarget, setJoinTarget] = useState<
        { collectionId: string; name: string } | undefined
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
                joinCollections={joinCards}
                onJoinCardClick={(collectionId, title) =>
                    setJoinTarget({ collectionId, name: title })
                }
                css={css`
                    flex-grow: 1;
                    min-width: 0;
                    overflow-y: auto;
                `}
            />
            {joinTarget && (
                <JoinCloudCollectionDialog
                    collectionId={joinTarget.collectionId}
                    collectionName={joinTarget.name}
                    signedIn={loginState.signedIn}
                    // Listed as a join card at all only because the signed-in user is approved
                    // for it (collections/getJoinCards, backed by collections/mine), so this is
                    // known-true here.
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
