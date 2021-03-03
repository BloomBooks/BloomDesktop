import React = require("react");
import { BloomApi } from "../utils/bloomApi";
import { BooksOfCollection } from "./BooksOfCollection";

export const CollectionsPane: React.FunctionComponent<{}> = () => {
    const [collections] = BloomApi.useApiJson("collections/list");

    if (collections) {
        const sourcesCollections = collections.slice(1);
        return (
            <div>
                {/* {JSON.stringify(books)} */}
                <h1>{collections[0].name}</h1>
                <BooksOfCollection collectionId={collections[0].id} />

                <h1>Sources For New Books</h1>
                {sourcesCollections.map(c => {
                    return (
                        <>
                            <h2>{c.name}</h2>
                            <BooksOfCollection collectionId={c.id} />
                        </>
                    );
                })}
            </div>
        );
    } else {
        return <h1>Loading...</h1>;
    }
};
