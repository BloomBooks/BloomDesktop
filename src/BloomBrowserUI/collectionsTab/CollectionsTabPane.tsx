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
import { useMemo, useState } from "react";
import useEventListener from "@use-it/event-listener";
import { BookSelectionManager } from "./bookSelectionManager";
import Delay from "../react_components/delay";
import { forceCheck as convertAnyVisibleLazyLoads } from "react-lazyload";

const kResizerSize = 10;

export const CollectionsTabPane: React.FunctionComponent<{}> = () => {
    const collections = BloomApi.useApiJson("collections/list");

    const [draggingSplitter, setDraggingSplitter] = useState(false);

    const manager: BookSelectionManager = useMemo(() => {
        const manager = new BookSelectionManager();
        manager.initialize();
        return manager;
    }, []);

    // There's no event built into the splitter that will tell us when drag is done.
    // So to tell that it's over, we have a global listener for mouseup.
    // useEventListener handles cleaning up the listener when this component is disposed.
    // The small delay somehow prevents a problem where the drag would continue after
    // mouse up. My hypothesis is that causing a re-render during mouse-up handling
    // interferes with the implementation of the splitter and causes it to miss
    // mouse up events, perhaps especially if outside the splitter control itself.
    useEventListener("mouseup", () => {
        if (draggingSplitter) {
            // LazyLoad isn't smart enough to notice that the size of the parent scrolling box changed.
            // We help out by telling it to check for anything that might have become visible
            // because of the drag.
            convertAnyVisibleLazyLoads();
        }
        setTimeout(() => setDraggingSplitter(false), 0);
    });

    if (collections) {
        const sourcesCollections = collections.slice(1);
        const collectionComponents = sourcesCollections.map(c => {
            return (
                <div key={"frag:" + c.id}>
                    <h2>{c.name}</h2>
                    <BooksOfCollection
                        collectionId={c.id}
                        isEditableCollection={false}
                        manager={manager}
                        lazyLoadCollection={true}
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
                            setDraggingSplitter(true);
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
                        hooks={{
                            onDragStarted: () => {
                                setDraggingSplitter(true);
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
                                manager={manager}
                                lazyLoadCollection={false}
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

                                    <Delay
                                        waitBeforeShow={100} // REview: we really want to wait for an event that indicates the main collection is mostly painted
                                    >
                                        {collectionComponents}
                                    </Delay>
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
                        <Delay
                            waitBeforeShow={500} // Review: we really want an event that indicates the collection panes are mostly painted.
                        >
                            <CollectionsTabBookPane
                                // While we're dragging the splitter, we need to overlay the iframe book preview
                                // so it doesn't steal the mouse events we need for dragging the splitter.
                                disableEventsInIframe={draggingSplitter}
                            />
                        </Delay>
                    </div>
                </SplitPane>
            </div>
        );
    } else {
        return <div />;
    }
};

WireUpForWinforms(CollectionsTabPane);
