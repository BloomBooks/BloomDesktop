import { css } from "@emotion/react";
import { post, useApiData } from "../utils/bloomApi";
import { CollectionCardList } from "./CollectionCardList";
import { ICollectionInfo } from "./CollectionCard";
import { MyCloudCollectionsSection } from "./MyCloudCollectionsSection";
import {
    pullDownCollection,
    useIsCloudTeamCollectionsExperimentalFeatureEnabled,
    useMyCloudCollections,
    useSharingLoginState,
} from "../teamCollection/sharingApi";

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
                    onPullDown={(collectionId) =>
                        pullDownCollection(collectionId)
                    }
                />
            )}
        </div>
    );
};
