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
import { getMasterToolList, ITool } from "./toolbox";
import { kToolboxHeaderZIndex } from "./toolboxZIndexes";
import { useMountEffect } from "../../utils/useMountEffect";
import {
    getToolboxUiState,
    setActiveTool,
    setToolboxUiMounted,
    subscribeToToolboxUiState,
} from "./toolboxState";
import { SubscriptionBadgeWithTooltipAndDialog } from "../../react_components/requiresSubscription";
import {
    getToolLabelInfo,
    kTalkingBookToolId,
    toPersistedToolName,
} from "./toolIds";
import { useToolLifecycle } from "./useToolLifecycle";

// React host for the toolbox sidebar. It shows an accordion for each tool the toolbox is
// offering, and opens the active one.
//
// It does not decide which tools to offer, nor own that fact: toolbox.ts asks the server
// which tools the book has enabled and records them in the toolbox state store
// (toolboxState.ts), which we subscribe to here.
//
// Every tool is a React component, and we render each one as an ordinary child (from its
// ITool.renderPanel()), so the whole toolbox is a single React tree: context such as the
// MUI theme reaches the tools normally. A closed tool keeps its children mounted (MUI's
// default), so a tool keeps its state as the user opens and closes tools.
//
// Rendering a tool is also what runs it: each one runs its tool's lifecycle
// from an effect for as long as that tool is the current tool of a showing toolbox. See
// useToolLifecycle.ts.

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
    // The tool itself, so that we can render its panel (ITool.renderPanel()) as our child.
    tool: ITool;
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

// Gathers everything we need in order to show this tool. The tool must be one the
// toolbox knows about: toolbox.ts only offers tools it found in the master list.
const makeOfferedTool = (toolId: string): OfferedTool => {
    const tool = getMasterToolList().find(
        (candidate) => candidate.id() === toolId,
    )!;
    const labelInfo = getToolLabelInfo(toolId);

    return {
        id: toolId,
        englishLabel: labelInfo.englishLabel,
        l10nKey: labelInfo.l10nKey,
        iconPath: tool.iconPath(),
        featureName: tool.featureName,
        tool: tool,
    };
};

// One tool as the toolbox shows it: its header, its panel, and its lifecycle.
//
// The lifecycle hook lives here, in the component that *contains* the panel, rather than
// beside it: React runs a child's effects before its parent's, so by the time we tell the
// tool to show itself its panel has mounted, as it always had under the old imperative
// order.
const OfferedToolAccordion: React.FunctionComponent<{
    offeredTool: OfferedTool;
    // Is this the open tool? Purely how the accordion looks.
    isOpen: boolean;
    // Is this the tool that is running? Not the same thing as being open; see
    // IToolboxUiState.currentToolId.
    isRunning: boolean;
    pageGeneration: number;
}> = (props) => {
    useToolLifecycle(
        props.offeredTool.tool,
        props.isRunning,
        props.pageGeneration,
    );

    return (
        <Accordion
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

                &.Mui-expanded > .MuiCollapse-root > .MuiCollapse-wrapper,
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
            expanded={props.isOpen}
            onChange={(_event, expanded) => {
                // Clicking the open tool's header does nothing, as in 6.5 and earlier
                // (originally BL-16533). With the legacy sync gone the collapse would
                // now work cleanly, but we decided (2026-09-11, on the BL-16608 review)
                // to keep the established behavior: the toolbox always shows one open
                // tool. The store itself still supports a state with no open tool — the
                // withdraw paths produce it — this just offers no gesture for it.
                if (expanded) {
                    setActiveTool(props.offeredTool.id);
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
                        props.offeredTool.id === kTalkingBookToolId
                            ? [
                                  toolboxHeaderIconStyles,
                                  css`
                                      width: 12px;
                                      background-size: 12px 16px;
                                  `,
                              ]
                            : toolboxHeaderIconStyles
                    }
                    data-toolid={props.offeredTool.id}
                    data-testid="toolbox-header-icon"
                    // The icon path is also exposed as data so tests can
                    // check which icon a header shows without reading styles.
                    data-icon-src={props.offeredTool.iconPath}
                    style={
                        props.offeredTool.iconPath
                            ? {
                                  backgroundImage: `url(${props.offeredTool.iconPath})`,
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
                    <LocalizedString l10nKey={props.offeredTool.l10nKey}>
                        {props.offeredTool.englishLabel}
                    </LocalizedString>
                </Typography>
                {props.offeredTool.featureName && (
                    <span>
                        <SubscriptionBadgeWithTooltipAndDialog
                            featureName={props.offeredTool.featureName}
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
                    // Some tool stylesheets, and our automated tests,
                    // still select a tool's body by this attribute, using
                    // the historical "Tool"-suffixed name.
                    data-toolid={toPersistedToolName(props.offeredTool.id)}
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
                        &[data-toolid="leveledReaderTool"],
                        &[data-toolid="decodableReaderTool"] {
                            padding-left: 3px;
                            box-sizing: border-box;
                        }

                        // Tools expect their root element to fill the
                        // space the toolbox gives them; several of them
                        // then use height:100% internally to push a Help
                        // link to the bottom.
                        > * {
                            width: 100%;
                            height: 100%;
                            min-width: 0;
                            flex: 1 1 auto;
                            display: block;
                        }
                    `}
                >
                    {props.offeredTool.tool.renderPanel()}
                </div>
            </AccordionDetails>
        </Accordion>
    );
};

// This component is the root of the whole toolbox sidebar. It is rendered into a dedicated
// host element created by the toolbox page pug.
export const ToolboxRoot: React.FunctionComponent = () => {
    const toolboxUiState = React.useSyncExternalStore(
        subscribeToToolboxUiState,
        getToolboxUiState,
    );
    // The store keeps the offered tools in the order we show them (alphabetical by label,
    // with "More..." last), because withdrawing the active tool has to know which tool
    // replaces it.
    const offeredTools = toolboxUiState.offeredToolIds.map(makeOfferedTool);
    const openToolId = toolboxUiState.activeToolId;

    // Let the rest of the toolbox know whether there is a toolbox UI at all; see
    // IToolboxUiState.uiMounted.
    useMountEffect(() => {
        setToolboxUiMounted(true);
        return () => {
            setToolboxUiMounted(false);
        };
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
                        // collapsed tool headers, as it did in 6.3 and earlier (BL-16532).
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
                    {offeredTools.map((offeredTool) => (
                        <OfferedToolAccordion
                            key={offeredTool.id}
                            offeredTool={offeredTool}
                            isOpen={openToolId === offeredTool.id}
                            isRunning={
                                toolboxUiState.currentToolId ===
                                    offeredTool.id &&
                                toolboxUiState.toolboxVisible
                            }
                            pageGeneration={toolboxUiState.pageGeneration}
                        />
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
