/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import React = require("react");
import { BloomApi } from "../utils/bloomApi";
import { BooksOfCollection } from "./BooksOfCollection";
import { Transition } from "react-transition-group";
import { SplitPane } from "react-collapse-pane";
import { kPanelBackground, kDarkestBackground } from "../bloomMaterialUITheme";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import { CollectionsTabBookPane } from "./collectionsTabBookPane/CollectionsTabBookPane";

const kResizerSize = 10;

export const CollectionsTabPane: React.FunctionComponent<{}> = () => {
    const [collections] = BloomApi.useApiJson("collections/list");

    if (collections) {
        const sourcesCollections = collections.slice(1);
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
                    height: 100vh; // I don't understand why 100% doesn't do it (nor why 100vh over-does it)
                    background-color: ${kPanelBackground};
                    color: white;

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

                    .SplitPane {
                        position: relative !important; // we may find this messes things up... but the "absolute" positioning default is ridiculous.

                        .Pane.horizontal {
                            overflow-y: scroll;
                            // TODO: this should only be applied to the bottom pane. As is, it pushes the top one away from the top of the screen.
                            margin-top: ${kResizerSize}px; // have to push down to make room for the resizer! Ughh.
                        }
                        .Pane.vertical {
                        }
                    }
                `}
            >
                <SplitPane
                    split="vertical"
                    resizerOptions={{
                        css: {
                            width: `${kResizerSize}px`,
                            background: `${kDarkestBackground}`
                        },
                        hoverCss: {
                            width: `${kResizerSize}px`,
                            background: `${kDarkestBackground}`
                        }
                    }}
                >
                    <SplitPane
                        split="horizontal"
                        // TODO: the splitter library lets us specify a height, but it doesn't apply it correctly an so the pane that follows
                        // does not get pushed down to make room for the thicker resizer
                        resizerOptions={{
                            css: {
                                height: `${kResizerSize}px`,
                                background: `${kDarkestBackground}`
                            },
                            hoverCss: {
                                height: `${kResizerSize}px`,
                                background: `${kDarkestBackground}`
                            }
                        }}
                    >
                        <div
                            css={css`
                                margin: 10px;
                            `}
                        >
                            <h1>{collections[0].name}</h1>

                            <BooksOfCollection
                                collectionId={collections[0].id}
                            />
                        </div>

                        <Transition in={true} appear={true} timeout={2000}>
                            {state => (
                                <div
                                    css={css`
                                        margin: 10px;
                                    `}
                                    className={`group fade-${state}`}
                                >
                                    <h1>Sources For New Books</h1>
                                    {collectionComponents}
                                </div>
                            )}
                        </Transition>
                    </SplitPane>
                    <div
                        css={css`
                            height: 100%;
                            margin: 10px;

                            margin-left: ${10 +
                                kResizerSize}px; // sigh. has to be left to make room for the splitter. I hate this splitter library!
                        `}
                    >
                        <CollectionsTabBookPane />
                    </div>
                </SplitPane>
            </div>
        );
    } else {
        return <div />;
    }
};

WireUpForWinforms(CollectionsTabPane);
