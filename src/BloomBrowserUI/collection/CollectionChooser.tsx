/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import FileFindIcon from "@mui/icons-material/FindInPage";
import BloomButton from "../react_components/bloomButton";
import { H3 } from "../react_components/l10nComponents";
import {
    DialogBottomButtons,
    DialogBottomLeftButtons
} from "../react_components/BloomDialog/BloomDialog";
import { useApiData } from "../utils/bloomApi";
import { CollectionCardList } from "./CollectionCardList";
import { ICollectionInfo } from "./CollectionCard";

export const CollectionChooser: React.FunctionComponent<{
    collections?: ICollectionInfo[];
}> = props => {
    let collections = props.collections || [];
    const collectionsFromApi = useApiData<ICollectionInfo[]>(
        "collections/getMostRecentlyUsedCollections",
        []
    );
    if (!props.collections?.length) collections = collectionsFromApi;

    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                height: 100%;
            `}
        >
            <H3
                l10nKey={"OpenCreateNewCollectionsDialog.ExistingCollections"}
                css={css`
                    font-weight: 400;
                `}
            >
                Existing Collections
            </H3>
            <CollectionCardList
                collections={collections}
                css={css`
                    flex-grow: 1;
                    overflow-y: auto;
                `}
            />
            <DialogBottomButtons>
                <DialogBottomLeftButtons>
                    <BloomButton
                        variant="text"
                        color="primary"
                        enabled={true}
                        l10nKey={
                            "OpenCreateNewCollectionsDialog.BrowseForOtherCollections"
                        }
                        startIcon={<FileFindIcon />}
                    >
                        Browse for another collection on this computer
                    </BloomButton>
                </DialogBottomLeftButtons>
                <BloomButton
                    variant="contained"
                    color="primary"
                    enabled={true}
                    l10nKey={
                        "OpenCreateNewCollectionsDialog.CreateNewCollection"
                    }
                >
                    Create New Collection
                </BloomButton>
            </DialogBottomButtons>
        </div>
    );
};
