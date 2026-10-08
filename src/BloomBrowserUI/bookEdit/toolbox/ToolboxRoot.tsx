import * as React from "react";
import { renderRoot } from "../../utils/reactRender";
import { css } from "@emotion/react";
import Accordion from "@mui/material/Accordion";
import AccordionDetails from "@mui/material/AccordionDetails";
import AccordionSummary from "@mui/material/AccordionSummary";
import Typography from "@mui/material/Typography";
import { ThemeProvider } from "@mui/material/styles";
import { toolboxTheme } from "../../bloomMaterialUITheme";
import { LocalizedString } from "../../react_components/l10nComponents";
import {
    kBloomBlue,
    kBloomPanelBackground,
    kBloomUnselectedTabBackground,
} from "../../utils/colorUtils";
import { getMasterToolList } from "./toolbox";
import { kToolboxHeaderZIndex } from "./toolboxZIndexes";
import { useMountEffect } from "../../utils/useMountEffect";
import { setToolboxReactAdapter } from "./toolboxReactAdapter";
import { SubscriptionBadgeWithTooltipAndDialog } from "../../react_components/requiresSubscription";
import {
    compareToolsByLabel,
    getToolLabelInfo,
    kSettingsToolId,
    kTalkingBookToolId,
    toPersistedToolName,
} from "./toolIds";

// React host for the toolbox sidebar. It holds the list of tools the toolbox is offering,
// which one is open, and the DOM node that each tool renders itself into.
//
// It does not decide which tools to offer: toolbox.ts asks the server which tools the book
// has enabled and tells us about each one through the adapter's addTool(), which is the
// only way a tool is ever created.
//
// Every tool is a React component, but a tool hands us the already-rendered root DOM
// element of its component (from its ITool.makeRootElement()) rather than an element type
// we could render ourselves. So a small host component (ToolBodyHost) puts that element
// into the React layout, which also means a tool keeps its state as the user opens and
// closes tools.

// Everything the toolbox needs in order to show one tool. It all comes from the
// tool itself (see ITool) or is derived from its id (see toolIds.ts).
type OfferedTool = {
    // The tool's canonical id, i.e. what its ITool.id() returns, e.g. "canvas".
    id: string;
    englishLabel: string;
    l10nKey: string;
    // The icon to show in the tool's header; undefined for tools without one.
    iconPath?: string;
    // Set only for tools that require a subscription, in which case the tool's header
    // gets a badge for this feature.
    featureName?: string;
    // The element the tool renders itself into. Created once, when the tool is first offered.
    toolBodyElement: HTMLDivElement;
};

const toolboxHeaderIconStyles = css`
    width: 16px;
    height: 16px;
    display: inline-block;
    background-position: center;
    background-repeat: no-repeat;
    background-size: contain;
    flex-shrink: 0;
`;

// Each tool's body, kept for the life of the toolbox and reused whenever the tool is offered
// again. makeRootElement() mounts the tool's own React root inside the element it returns, and
// withdrawing a tool only detaches that element: the root stays mounted, so its effects are
// never cleaned up. Making a fresh one each time the user ticks the tool in More... would
// therefore leave the old one running (the Canvas tool polls on a timer, for instance) and add
// another alongside it. The legacy toolbox likewise built each tool's body only once.
const toolBodyElements = new Map<string, HTMLDivElement>();

// Gathers everything we need in order to show this tool. The tool must be one the
// toolbox knows about: toolbox.ts only asks us for tools it found in the master list.
const makeOfferedTool = (toolId: string): OfferedTool => {
    const tool = getMasterToolList().find(
        (candidate) => candidate.id() === toolId,
    )!;
    const labelInfo = getToolLabelInfo(toolId);
    const toolBodyElement =
        toolBodyElements.get(toolId) ?? tool.makeRootElement();
    toolBodyElements.set(toolId, toolBodyElement);
    // Some tool stylesheets still select their body by this attribute, using the
    // historical "Tool"-suffixed name.
    toolBodyElement.setAttribute("data-toolid", toPersistedToolName(toolId));

    return {
        id: toolId,
        englishLabel: labelInfo.englishLabel,
        l10nKey: labelInfo.l10nKey,
        iconPath: tool.iconPath(),
        featureName: tool.featureName,
        toolBodyElement: toolBodyElement,
    };
};

const sortToolsAlphabeticallyWithSettingsLast = (
    offeredTools: OfferedTool[],
): OfferedTool[] => {
    const settingsTool = offeredTools.find(
        (tool) => tool.id === kSettingsToolId,
    );
    const nonSettingsTools = offeredTools
        .filter((tool) => tool.id !== kSettingsToolId)
        .sort((a, b) => compareToolsByLabel(a.id, b.id));

    if (!settingsTool) {
        return nonSettingsTools;
    }

    return [...nonSettingsTools, settingsTool];
};

// Puts a tool's own DOM element (the one it renders itself into) into the React layout,
// keeping the original element instance so the tool's state and event wiring stay intact.
const ToolBodyHost: React.FunctionComponent<{ element: HTMLDivElement }> = (
    props,
) => {
    const hostRef = React.useRef<HTMLDivElement | null>(null);

    React.useEffect(() => {
        const host = hostRef.current;
        if (!host) {
            return;
        }

        if (!host.contains(props.element)) {
            host.appendChild(props.element);
        }

        return () => {
            if (host.contains(props.element)) {
                host.removeChild(props.element);
            }
        };
    }, [props.element]);

    return (
        <div
            ref={hostRef}
            css={css`
                width: 100%;
                height: 100%;
                flex: 1;
                display: flex;
                flex-direction: column;
                align-items: stretch;
                min-height: 0;
                min-width: 0;

                // Tools expect their root element to fill the space the toolbox gives
                // them; several of them then use height:100% internally to push a Help
                // link to the bottom.
                > * {
                    width: 100%;
                    height: 100%;
                    min-width: 0;
                    flex: 1 1 auto;
                    display: block;
                }
            `}
        ></div>
    );
};

// This component is the root of the whole toolbox sidebar. It is rendered into a dedicated
// host element created by the toolbox page pug.
export const ToolboxRoot: React.FunctionComponent = () => {
    const [offeredTools, setOfferedTools] = React.useState<OfferedTool[]>([]);
    const [openToolId, setOpenToolId] = React.useState<string>();
    const activeToolChangedCallbacks = React.useRef<
        ((toolId: string) => void)[]
    >([]);
    // The authoritative copy of the tools being offered, so that the adapter methods toolbox.ts
    // calls can read and update the list synchronously. (React state is updated from it,
    // for rendering.)
    const offeredToolsRef = React.useRef<OfferedTool[]>([]);
    // Likewise the authoritative copy of which tool is open, so that removeTool()
    // can tell synchronously whether it is removing the open one.
    const openToolIdRef = React.useRef<string | undefined>(undefined);

    const applyOfferedTools = React.useCallback((nextTools: OfferedTool[]) => {
        offeredToolsRef.current = nextTools;
        setOfferedTools(nextTools);
    }, []);

    const setOpenTool = React.useCallback((toolId: string | undefined) => {
        openToolIdRef.current = toolId;
        setOpenToolId(toolId);
    }, []);

    // Open this tool and tell toolbox.ts about it. toolbox.ts keeps its own
    // idea of which tool is current and drives each tool's showTool()/hideTool() from it,
    // so every path that changes which tool is open to a real tool has to come
    // through here; one that quietly changed only our state left the two out of sync and
    // the tool the user could see was never activated (BL-16602).
    const makeToolActive = React.useCallback(
        (toolId: string) => {
            setOpenTool(toolId);
            activeToolChangedCallbacks.current.forEach((callback) => {
                callback(toolId);
            });
        },
        [setOpenTool],
    );

    // Register the adapter that toolbox.ts uses to say which tools the toolbox offers,
    // to make one of them active, and to observe which one is active.
    // See toolboxReactAdapter.ts.
    useMountEffect(() => {
        setToolboxReactAdapter({
            setActiveToolByToolId: (toolId: string) => {
                makeToolActive(toolId);
            },
            onActiveToolChanged: (callback: (toolId: string) => void) => {
                activeToolChangedCallbacks.current.push(callback);
            },
            addTool: (toolId: string) => {
                if (
                    offeredToolsRef.current.some((tool) => tool.id === toolId)
                ) {
                    return;
                }
                applyOfferedTools(
                    sortToolsAlphabeticallyWithSettingsLast([
                        ...offeredToolsRef.current,
                        makeOfferedTool(toolId),
                    ]),
                );
            },
            removeTool: (toolId: string) => {
                const remainingTools = offeredToolsRef.current.filter(
                    (tool) => tool.id !== toolId,
                );
                if (remainingTools.length === offeredToolsRef.current.length) {
                    return;
                }
                applyOfferedTools(remainingTools);
                if (openToolIdRef.current !== toolId) {
                    // We removed a tool the user wasn't looking at, so which tool is
                    // open doesn't change.
                    return;
                }
                const replacementToolId = remainingTools[0]?.id;
                if (!replacementToolId) {
                    // Nothing left to open. Don't notify toolbox.ts: it has no way to
                    // represent "no current tool", and opening a tool later will
                    // tell it then.
                    setOpenTool(undefined);
                    return;
                }
                // Go through makeToolActive so toolbox.ts hears about the replacement.
                // Leaving a game page removes the Game tool this way, and when this
                // didn't notify, toolbox.ts went on believing Game was current and
                // never called showTool() on the tool that replaced it, which killed
                // Talking Book's highlighting and audio (BL-16602).
                makeToolActive(replacementToolId);
            },
            hasTool: (toolId: string) => {
                return offeredToolsRef.current.some(
                    (tool) => tool.id === toolId,
                );
            },
            getFirstToolId: () => {
                return offeredToolsRef.current.find(
                    (tool) => tool.id !== kSettingsToolId,
                )?.id;
            },
        });
    });

    return (
        <div
            css={css`
                height: 100%;
                display: flex;
                flex-direction: column;
                // This overrides a font-size: x-small that is set on div.toolboxRoot
                // (it replaces something that we somehow inherited from a jquery stylesheet
                // in earlier versions of Bloom)
                font-size: 11px;
            `}
        >
            <ThemeProvider theme={toolboxTheme}>
                <div
                    css={css`
                        border-bottom: 1px solid rgba(255, 255, 255, 0.2);
                        background-color: ${kBloomPanelBackground};
                        display: flex;
                        flex-direction: column;
                        // Lets the darker panel background show through between the
                        // closed tool headers, as it did in 6.3 and earlier (BL-16532).
                        gap: 1px;
                        height: 100%;
                        min-height: 0;

                        a {
                            color: white;
                        }

                        .helpLinkWrapper a {
                            color: white;
                        }
                    `}
                >
                    {offeredTools.map((tool) => (
                        <Accordion
                            key={tool.id}
                            css={css`
                                background-color: ${kBloomUnselectedTabBackground};
                                color: white;
                                margin: 0;
                                display: flex;
                                flex-direction: column;
                                flex-shrink: 0;

                                &:before {
                                    display: none;
                                }

                                &.Mui-expanded {
                                    background-color: ${kBloomPanelBackground};
                                    flex: 1 1 auto;
                                    min-height: 0;
                                }

                                &.Mui-expanded > .MuiCollapse-root {
                                    display: flex;
                                    flex-direction: column;
                                    flex: 1;
                                    min-height: 0;
                                    overflow: hidden;
                                }

                                &.Mui-expanded
                                    > .MuiCollapse-root
                                    > .MuiCollapse-wrapper,
                                &.Mui-expanded
                                    > .MuiCollapse-root
                                    > .MuiCollapse-wrapper
                                    > .MuiCollapse-wrapperInner,
                                &.Mui-expanded
                                    > .MuiCollapse-root
                                    > .MuiCollapse-wrapper
                                    > .MuiCollapse-wrapperInner
                                    > .MuiAccordion-region {
                                    display: flex;
                                    flex-direction: column;
                                    flex: 1;
                                    min-height: 0;
                                    overflow: hidden;
                                }
                            `}
                            disableGutters
                            expanded={openToolId === tool.id}
                            onChange={(_event, expanded) => {
                                // Clicking the open tool's header does nothing, as in 6.5 and earlier
                                // (originally BL-16533). With the legacy sync gone the collapse would
                                // now work cleanly, but we decided (2026-09-11, on the BL-16608 review)
                                // to keep the established behavior: the toolbox always shows one open
                                // tool.
                                if (expanded) {
                                    makeToolActive(tool.id);
                                }
                            }}
                        >
                            <AccordionSummary
                                css={css`
                                    min-height: 32px;
                                    padding-left: 5px;
                                    padding-right: 12px;
                                    // Keep the headers above the Talking Book tool's disabling
                                    // overlay, so they neither look grayed out nor stop
                                    // responding in Show Playback Order mode (BL-16630); see
                                    // toolboxZIndexes.ts for where the number comes from.
                                    // Only works while no ancestor creates a stacking context
                                    // -- a transform, filter, opacity or z-index on the
                                    // Accordion, the Collapse or the tool-body host would
                                    // trap it.
                                    position: relative;
                                    z-index: ${kToolboxHeaderZIndex};
                                    // The header has to paint its own background for that to
                                    // help. A collapsed header would otherwise be transparent
                                    // and show the Accordion root's background, which stays
                                    // under the overlay and so keeps being dimmed. Same colour
                                    // the root uses, so nothing changes visually.
                                    background-color: ${kBloomUnselectedTabBackground};

                                    & .MuiAccordionSummary-content {
                                        margin: 8px 0;
                                        display: flex;
                                        align-items: center;
                                        gap: 12px;
                                    }

                                    &.Mui-expanded {
                                        min-height: 32px;
                                        background-color: ${kBloomBlue};
                                    }
                                `}
                            >
                                <span
                                    // The talking book icon is a tall, narrow microphone,
                                    // so it gets a narrower box than the others.
                                    css={
                                        tool.id === kTalkingBookToolId
                                            ? [
                                                  toolboxHeaderIconStyles,
                                                  css`
                                                      width: 12px;
                                                      background-size: 12px 16px;
                                                  `,
                                              ]
                                            : toolboxHeaderIconStyles
                                    }
                                    data-toolid={tool.id}
                                    data-testid="toolbox-header-icon"
                                    // The icon path is also exposed as data so tests can
                                    // check which icon a header shows without reading styles.
                                    data-icon-src={tool.iconPath}
                                    style={
                                        tool.iconPath
                                            ? {
                                                  backgroundImage: `url(${tool.iconPath})`,
                                              }
                                            : undefined
                                    }
                                ></span>
                                <Typography
                                    css={css`
                                        flex-grow: 1;
                                        font-size: 11px;
                                    `}
                                >
                                    <LocalizedString l10nKey={tool.l10nKey}>
                                        {tool.englishLabel}
                                    </LocalizedString>
                                </Typography>
                                {tool.featureName && (
                                    <span>
                                        <SubscriptionBadgeWithTooltipAndDialog
                                            featureName={tool.featureName}
                                        />
                                    </span>
                                )}
                            </AccordionSummary>
                            <AccordionDetails
                                css={css`
                                    background-color: ${kBloomPanelBackground};
                                    padding: 0;
                                    flex: 1;
                                    display: flex;
                                    min-height: 0;
                                    overflow: auto;
                                `}
                            >
                                <div
                                    css={css`
                                        width: 100%;
                                        display: flex;
                                        flex-direction: column;
                                        align-items: stretch;
                                        min-height: 100%;
                                        overflow: visible;

                                        // The Decodable and Leveled reader tool bodies
                                        // were laid out to suit the small left padding
                                        // that the old jQuery-UI accordion content panels
                                        // gave them, so keep that.
                                        div[data-toolid="leveledReaderTool"],
                                        div[data-toolid="decodableReaderTool"] {
                                            padding-left: 3px;
                                            box-sizing: border-box;
                                        }
                                    `}
                                >
                                    <ToolBodyHost
                                        element={tool.toolBodyElement}
                                    />
                                </div>
                            </AccordionDetails>
                        </Accordion>
                    ))}
                </div>
            </ThemeProvider>
        </div>
    );
};

export const renderToolboxRoot = (): void => {
    // Bootstraps the React toolbox into the dedicated host element created by
    // the toolbox page markup.
    const hostElement = document.getElementById("toolbox-react-root");
    if (!hostElement) {
        return;
    }

    hostElement.style.height = "100%";
    hostElement.style.display = "flex";
    hostElement.style.flexDirection = "column";

    renderRoot(<ToolboxRoot />, hostElement);
};
