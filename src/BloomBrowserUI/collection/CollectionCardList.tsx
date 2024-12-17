/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import { CollectionCard, ICollectionInfo } from "./CollectionCard";

export const CollectionCardList: React.FunctionComponent<{
    collections: ICollectionInfo[];
    className?: string;
}> = props => {
    const gap = "16px";
    const gridStyle = css`
        display: grid;
        grid-template-columns: repeat(2, 1fr);
        gap: ${gap};
        overflow-y: auto;
        // Make sure the last cards' shadow is visible
        padding-bottom: 20px;
        // Pad the right side if there is a scrollbar
        // Enhance: make this smarter by actually checking if there is a scrollbar
        padding-right: ${props.collections.length > 6 ? gap : 0};
    `;

    const maxCardCount = 10;
    const itemsToShow = props.collections?.length
        ? props.collections.slice(0, maxCardCount)
        : [];

    return (
        <div className={props.className}>
            <div css={gridStyle}>
                {itemsToShow.map((cardInfo, _) => (
                    <CollectionCard key={cardInfo.path} {...cardInfo} />
                ))}
            </div>
        </div>
    );
};
