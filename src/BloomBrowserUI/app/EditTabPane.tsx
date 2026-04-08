import * as React from "react";
import { css } from "@emotion/react";
import { get } from "../utils/bloomApi";
import { setToolboxEnabledHandler } from "../bookEdit/workspaceRoot";

interface IEditFrameSources {
    pageListSrc: string;
    pageSrc: string;
    toolboxSrc: string;
    toolboxIsShowing?: boolean;
}

const defaultEditFrameSources: IEditFrameSources = {
    pageListSrc: "about:blank",
    pageSrc: "about:blank",
    toolboxSrc: "/bloom/toolboxContent",
};

export const EditTabPane: React.FunctionComponent<{ active: boolean }> = (
    props,
) => {
    const [sources, setSources] = React.useState<IEditFrameSources>(
        defaultEditFrameSources,
    );
    const [toolboxIsShowing, setToolboxIsShowing] = React.useState(true);
    const [toolboxIsEnabled, setToolboxIsEnabled] = React.useState(true);

    // Request edit-frame URLs from C# when edit mode becomes active so app.tsx controls iframe rendering.
    React.useEffect(() => {
        if (!props.active) {
            return;
        }

        get("editView/frameSources", (result) => {
            const data =
                typeof result.data === "string"
                    ? (JSON.parse(result.data) as IEditFrameSources)
                    : (result.data as IEditFrameSources);
            setSources(data);
            setToolboxIsShowing(data.toolboxIsShowing ?? true);
        });
    }, [props.active]);

    React.useEffect(() => {
        // We will use this to disable and hide the toolbox when in Change Layout mode (BL-16069)
        setToolboxEnabledHandler((enabled) => {
            setToolboxIsEnabled(enabled);
            if (!enabled) {
                setToolboxIsShowing(false);
            }
        });

        return () => {
            setToolboxEnabledHandler(undefined);
        };
    }, []);

    return (
        <div
            css={css`
                height: 100%;
                min-height: 0;
                overflow: hidden;

                #left,
                #right {
                    width: 100%;
                    box-sizing: border-box;
                    height: 100%;
                }
                // Continuing to use the pure-drawer approach for now to showing and hiding the toolbox.
                // I suggested that Copilot replace it with some MUI/React approach, but it did this anyway.
                // At this point, I'm not finding it very objectionable, and it may help keep the
                // expected behavior.
                #pusherContainer {
                    padding-right: 35px;
                    height: 100%;
                    box-sizing: border-box;
                    overflow: hidden;
                }

                .pure-pusher-container,
                .pure-pusher {
                    height: 100%;
                    min-height: 0;
                    overflow: hidden;
                }

                #left {
                    font-size: 0;
                    line-height: 0;
                }

                .pure-container {
                    display: block;
                    height: 100%;
                    position: relative;
                    z-index: 1;
                    overflow: hidden;
                }

                .pure-toggle-label {
                    position: absolute;
                    top: 3px;
                }

                .pure-toggle-label.toolbox-disabled {
                    opacity: 0.45;
                    cursor: default;
                }

                .pure-toggle-label.toolbox-disabled .pure-toggle-icon {
                    filter: grayscale(1);
                }

                .pure-drawer {
                    position: absolute;
                    top: 0;
                    bottom: 0;
                    height: auto;
                }

                .pure-overlay {
                    position: absolute;
                    top: 0;
                    bottom: 0;
                }

                [data-effect="pure-effect-slide"] div#pusherContainer {
                    transition-duration: 500ms;
                }

                .pure-toggle[data-toggle="right"]:checked
                    ~ .pure-pusher-container
                    div#pusherContainer {
                    padding-right: 200px;
                }

                iframe {
                    border: 0 none;
                }
            `}
        >
            <div className="pure-container" data-effect="pure-effect-slide">
                <input
                    className="pure-toggle"
                    type="checkbox"
                    id="pure-toggle-right"
                    data-toggle="right"
                    checked={toolboxIsShowing}
                    disabled={!toolboxIsEnabled}
                    onChange={(event) => {
                        if (!toolboxIsEnabled) {
                            return;
                        }
                        setToolboxIsShowing(event.currentTarget.checked);
                    }}
                />
                <label
                    className={`pure-toggle-label${
                        toolboxIsEnabled ? "" : " toolbox-disabled"
                    }`}
                    htmlFor="pure-toggle-right"
                    data-toggle-label="right"
                    aria-disabled={!toolboxIsEnabled}
                >
                    <span className="pure-toggle-icon" />
                </label>
                <nav className="pure-drawer" data-position="right">
                    <div id="right">
                        <iframe
                            id="toolbox"
                            title="toolbox"
                            width="100%"
                            height="100%"
                            src={sources.toolboxSrc}
                        >
                            Your browser does not support iframes.
                        </iframe>
                    </div>
                </nav>
                <div className="pure-pusher-container">
                    <div className="pure-pusher">
                        <div id="pusherContainer">
                            <div id="left">
                                <div
                                    css={css`
                                        width: 200px;
                                        height: 100%;
                                        display: inline-block;
                                        vertical-align: top;
                                        box-sizing: border-box;
                                    `}
                                >
                                    <iframe
                                        id="pageList"
                                        title="pageList"
                                        width="100%"
                                        height="100%"
                                        src={sources.pageListSrc}
                                    />
                                </div>
                                <div
                                    css={css`
                                        width: calc(100% - 205px);
                                        padding-left: 5px;
                                        height: 100%;
                                        display: inline-block;
                                        vertical-align: top;
                                        box-sizing: border-box;
                                    `}
                                >
                                    <iframe
                                        id="page"
                                        title="page"
                                        width="100%"
                                        height="100%"
                                        src={sources.pageSrc}
                                    />
                                </div>
                            </div>
                        </div>
                    </div>
                    <label
                        className="pure-overlay"
                        htmlFor="pure-toggle-right"
                        data-overlay="right"
                    />
                </div>
            </div>
        </div>
    );
};
