import { css } from "@emotion/react";
import { H3 } from "../react_components/l10nComponents";
import { useApiData } from "../utils/bloomApi";
import { CollectionCardList } from "./CollectionCardList";
import { ICollectionInfo } from "./CollectionCard";

export const CollectionChooser: React.FunctionComponent<{
    collections?: ICollectionInfo[];
}> = (props) => {
    let collections = props.collections || [];
    const collectionsFromApi = useApiData<ICollectionInfo[]>(
        "collections/getMostRecentlyUsedCollections",
        [],
    );
    if (!props.collections?.length) collections = collectionsFromApi;

    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                flex-grow: 1;
            `}
        >
            <CollectionCardList
                collections={collections}
                css={css`
                    flex-grow: 1;
                    overflow-y: auto;
                `}
            />
        </div>
    );
};
