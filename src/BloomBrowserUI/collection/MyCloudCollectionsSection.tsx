import { css } from "@emotion/react";
import * as React from "react";
import BloomButton from "../react_components/bloomButton";
import { Div, Span } from "../react_components/l10nComponents";
import { kBloomGray } from "../utils/colorUtils";
import {
    ICloudCollectionSummary,
    ISharingLoginState,
} from "../teamCollection/sharingApi";

// The "Get my Team Collections" sidebar of the collection chooser dialog: lists the cloud
// Team Collections the signed-in user has been approved for (claimed or not), each with a
// button to pull it down locally. Presentational: a pure function of its props, so the
// signed-out/loading/empty/listing states can be unit-tested without any network layer.
export const MyCloudCollectionsSection: React.FunctionComponent<{
    loginState: ISharingLoginState;
    collections: ICloudCollectionSummary[];
    loading: boolean;
    onSignInClick: () => void;
    onPullDown: (collectionId: string) => void;
}> = (props) => {
    return (
        <div
            data-testid="my-cloud-collections-section"
            css={css`
                display: flex;
                flex-direction: column;
                width: 220px;
                flex-shrink: 0;
                border-left: solid 1px #d9d9d9;
                padding-left: 16px;
                overflow-y: auto;
            `}
        >
            <Span
                l10nKey="CollectionChooser.GetMyTeamCollections"
                temporarilyDisableI18nWarning={true}
                css={css`
                    font-weight: 600;
                    margin-bottom: 8px;
                `}
            >
                Get my Team Collections
            </Span>
            {!props.loginState.signedIn ? (
                <div data-testid="my-cloud-collections-signed-out">
                    <Div
                        l10nKey="CollectionChooser.SignInToSeeYourTeamCollections"
                        temporarilyDisableI18nWarning={true}
                        css={css`
                            color: ${kBloomGray};
                            margin-bottom: 8px;
                        `}
                    >
                        Sign in to see the Team Collections you belong to.
                    </Div>
                    <BloomButton
                        enabled={true}
                        hasText={true}
                        variant="outlined"
                        l10nKey="CollectionChooser.SignIn"
                        temporarilyDisableI18nWarning={true}
                        data-testid="my-cloud-collections-signin-button"
                        onClick={props.onSignInClick}
                    >
                        Sign In
                    </BloomButton>
                </div>
            ) : props.loading ? (
                // The l10n Div/P/Span components don't forward arbitrary props like
                // data-testid to their rendered DOM node, so wrap in a plain div for that.
                <div data-testid="my-cloud-collections-loading">
                    <Div
                        l10nKey="Common.Loading"
                        css={css`
                            color: ${kBloomGray};
                        `}
                    >
                        Loading...
                    </Div>
                </div>
            ) : props.collections.length === 0 ? (
                <div data-testid="my-cloud-collections-empty">
                    <Div
                        l10nKey="CollectionChooser.NoTeamCollectionsYet"
                        temporarilyDisableI18nWarning={true}
                        css={css`
                            color: ${kBloomGray};
                        `}
                    >
                        You don't belong to any Team Collections yet.
                    </Div>
                </div>
            ) : (
                <div
                    data-testid="my-cloud-collections-list"
                    css={css`
                        display: flex;
                        flex-direction: column;
                        gap: 8px;
                    `}
                >
                    {props.collections.map((collection) => (
                        <div
                            key={collection.collectionId}
                            data-testid="my-cloud-collection-row"
                            data-collection-id={collection.collectionId}
                            css={css`
                                display: flex;
                                align-items: center;
                                justify-content: space-between;
                                gap: 8px;
                            `}
                        >
                            <span
                                css={css`
                                    overflow: hidden;
                                    text-overflow: ellipsis;
                                    white-space: nowrap;
                                `}
                            >
                                {collection.name}
                            </span>
                            <BloomButton
                                size="small"
                                enabled={true}
                                hasText={true}
                                variant="text"
                                l10nKey="CollectionChooser.PullDown"
                                temporarilyDisableI18nWarning={true}
                                data-testid="my-cloud-collection-pulldown-button"
                                onClick={() =>
                                    props.onPullDown(collection.collectionId)
                                }
                            >
                                Get
                            </BloomButton>
                        </div>
                    ))}
                </div>
            )}
        </div>
    );
};
