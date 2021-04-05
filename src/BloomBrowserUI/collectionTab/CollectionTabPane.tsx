import React = require("react");
import { BloomApi } from "../utils/bloomApi";
import { BooksOfCollection } from "./BooksOfCollection";
import { Transition } from "react-transition-group";
import { Split } from "@geoffcox/react-splitter";

export const CollectionsPane: React.FunctionComponent<{}> = () => {
    const [collections] = BloomApi.useApiJson("collections/list");

    if (collections) {
        const sourcesCollections = collections.slice(1);

        const splitterProps = {
            color: "black",
            hover: "black",
            drag: "black"
        };

        return (
            <div style={{ height: "100%" }}>
                {/* {JSON.stringify(books)} */} <h1>{collections[0].name}</h1>
                <Split
                    horizontal={true}
                    defaultSplitterColors={splitterProps}
                    splitterSize={"20px"}
                >
                    <BooksOfCollection collectionId={collections[0].id} />
                    <Transition in={true} appear={true} timeout={2000}>
                        {state => (
                            <div className={`group fade-${state}`}>
                                <h1>Sources For New Books</h1>
                                {sourcesCollections.map(c => {
                                    return (
                                        <>
                                            <h2>{c.name}</h2>
                                            <BooksOfCollection
                                                collectionId={c.id}
                                            />
                                        </>
                                    );
                                })}
                            </div>
                        )}
                    </Transition>
                </Split>
            </div>
        );
    } else {
        return <div />;
    }
};
