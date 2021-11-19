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
import { useState } from "react";
import useEventListener from "@use-it/event-listener";

const kResizerSize = 10;

export const CollectionsTabPane: React.FunctionComponent<{}> = () => {
    const collections = BloomApi.useApiJson("collections/list");

    const [draggingVSplitter, setDraggingVSplitter] = useState(false);

    // There's no event built into the splitter that will tell us when drag is done.
    // So to tell that it's over, we have a global listener for mouseup.
    // useEventListener handles cleaning up the listener when this component is disposed.
    useEventListener("mouseup", () => setDraggingVSplitter(false));

    if (collections) {
        const sourcesCollections = collections.slice(1);
        const collectionComponents = sourcesCollections.map(c => {
            return (
                <div key={"frag:" + c.id}>
                    <h2>{c.name}</h2>
                    <BooksOfCollection
                        collectionId={c.id}
                        isEditableCollection={false}
                    />
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
                            overflow-y: auto;
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
                    // This gives the two panes about the same ratio as in the Legacy view.
                    // Enhance: we'd like to save the user's chosen width.
                    initialSizes={[31, 50]}
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
                    hooks={{
                        onDragStarted: () => {
                            setDraggingVSplitter(true);
                        }
                    }}
                    // onDragFinished={() => {
                    //     alert("stopped dragging");
                    //     setDraggingVSplitter(false);
                    // }}
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
                                isEditableCollection={true}
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
                    {/* This wrapper is used to... fix up some margin/color stuff I was having trouble with from SplitPane */}
                    <div
                        css={css`
                            height: 100%;
                        `}
                    >
                        <CollectionsTabBookPane
                            // While we're dragging the splitter, we need to overlay the iframe book preview
                            // so it doesn't steal the mouse events we need for dragging the splitter.
                            disableEventsInIframe={draggingVSplitter}
                        />
                    </div>
                </SplitPane>
            </div>
        );
    } else {
        return <div />;
    }
};

WireUpForWinforms(CollectionsTabPane);
