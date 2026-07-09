import { css } from "@emotion/react";
import { CollectionCard, ICollectionInfo } from "./CollectionCard";

// One entry from collections/getJoinCards: a cloud Team Collection the signed-in user belongs to
// but has no local copy of yet (see CollectionChooserApi.HandleGetJoinCards).
export interface IJoinCollectionInfo {
    collectionId: string;
    title: string;
}

export const CollectionCardList: React.FunctionComponent<{
    collections: ICollectionInfo[];
    // Join cards (dogfood batch 1, item 6): always appended AFTER the maxCardCount slice below, so
    // they never count against the MRU card limit.
    joinCollections?: IJoinCollectionInfo[];
    onJoinCardClick?: (collectionId: string, title: string) => void;
    className?: string;
}> = (props) => {
    const gap = "16px";
    const joinCollections = props.joinCollections ?? [];
    const totalCardCount = props.collections.length + joinCollections.length;
    const gridStyle = css`
        display: grid;
        grid-template-columns: repeat(2, 1fr);
        gap: ${gap};
        overflow-y: auto;
        // Make sure the last cards' shadow is visible
        padding-bottom: 20px;
        // Pad the right side if there is a scrollbar
        // Enhance: make this smarter by actually checking if there is a scrollbar
        padding-right: ${totalCardCount > 6 ? gap : 0};
    `;

    const maxCardCount = 10;
    const itemsToShow = props.collections?.length
        ? props.collections.slice(0, maxCardCount)
        : [];
    const joinCardInfos: ICollectionInfo[] = joinCollections.map((join) => ({
        // No local path exists yet; a unique placeholder just for React's key/CollectionCard's
        // path prop (never used for opening -- join cards ignore it, see CollectionCard.tsx).
        path: `join:${join.collectionId}`,
        title: join.title,
        bookCount: 0,
        isTeamCollection: true,
        isJoinCard: true,
        collectionId: join.collectionId,
        onJoinClick: props.onJoinCardClick,
    }));

    return (
        <div className={props.className}>
            <div css={gridStyle}>
                {[...itemsToShow, ...joinCardInfos].map((cardInfo) => (
                    <CollectionCard key={cardInfo.path} {...cardInfo} />
                ))}
            </div>
        </div>
    );
};
