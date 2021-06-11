/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import React = require("react");
import { BloomApi } from "../utils/bloomApi";
import { BooksOfCollection } from "./BooksOfCollection";
import { Transition } from "react-transition-group";
import Splitter from "m-react-splitters";
import "m-react-splitters/lib/splitters.css";
//import "CollectionTabPane.less";
import { kPanelBackground, kDarkestBackground } from "../bloomMaterialUITheme";

export const CollectionsTabPane: React.FunctionComponent<{}> = () => {
    const [collections] = BloomApi.useApiJson("collections/list");

    if (collections) {
        const sourcesCollections = collections.slice(1);

        const splitterProps = {
            color: "black",
            hover: "black",
            drag: "black"
        };

        const collectionComponents = sourcesCollections.map(c => {
            return (
                <div key={"frag:" + c.id}>
                    <h2>{c.name}</h2>
                    <BooksOfCollection collectionId={c.id} />
                </div>
            );
        });

        return (
            <div
                css={css`
                    background-color: ${kPanelBackground};
                    color: white;
                    padding: 25px;
                    h1 {
                        font-size: 20px;
                        margin: 0;
                    }
                    h2 {
                        font-size: 16px;
                        margin: 0;
                    }

                    .handle-bar,
                    .handle-bar:hover {
                        background-color: ${kDarkestBackground};
                    }
                `}
            >
                <Splitter position="vertical">
                    <Splitter position="horizontal">
                        <div style={{ padding: "10px" }}>
                            <h1>{collections[0].name}</h1>

                            <BooksOfCollection
                                collectionId={collections[0].id}
                            />
                        </div>
                        <Transition in={true} appear={true} timeout={2000}>
                            {state => (
                                <div
                                    style={{ padding: "10px" }}
                                    className={`group fade-${state}`}
                                >
                                    {/*-${state} */}
                                    <h1>Sources For New Books</h1>
                                    {collectionComponents}
                                </div>
                            )}
                        </Transition>
                    </Splitter>

                    <h1>TODO: Preview</h1>
                </Splitter>
            </div>
        );
    } else {
        return <div />;
    }
};
