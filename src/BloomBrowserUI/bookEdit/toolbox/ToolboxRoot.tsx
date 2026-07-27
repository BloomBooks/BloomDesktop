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
// which one is expanded, and the DOM node that each tool renders itself into.
//
// It does not decide which tools to offer: toolbox.ts asks the server which tools the book
// has enabled and tells us about each one through the adapter's addTool(), which is the
// only way a section is ever created.
//
// Every tool is a React component, but a tool hands us the already-rendered root DOM
// element of its component (from its ITool.makeRootElement()) rather than an element type
// we could render ourselves. So a small host component (ToolBodyHost) puts that element
// into the React layout, which also means a tool keeps its state as sections open and
// close.

// Everything the toolbox needs in order to show one tool's section. It all comes from the
// tool itself (see ITool) or is derived from its id (see toolIds.ts).
type ToolboxSection = {
    // The tool's canonical id, i.e. what its ITool.id() returns, e.g. "canvas".
    id: string;
    englishLabel: string;
    l10nKey: string;
    // The icon to show in the section header; undefined for sections without one.
    iconPath?: string;
    // Set only for tools that require a subscription, in which case the section header
    // gets a badge for this feature.
    featureName?: string;
    // The element the tool renders itself into. Created once, when the section is created.
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

// Gathers everything we need to show a section for this tool. The tool must be one the
// toolbox knows about: toolbox.ts only asks us for tools it found in the master list.
const makeSectionFromToolId = (toolId: string): ToolboxSection => {
    const tool = getMasterToolList().find(
        (candidate) => candidate.id() === toolId,
    )!;
    const labelInfo = getToolLabelInfo(toolId);
    const toolBodyElement = tool.makeRootElement();
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

const sortSectionsAlphabeticallyWithSettingsLast = (
    sections: ToolboxSection[],
): ToolboxSection[] => {
    const settingsSection = sections.find(
        (section) => section.id === kSettingsToolId,
    );
    const nonSettingsSections = sections
        .filter((section) => section.id !== kSettingsToolId)
        .sort((a, b) => compareToolsByLabel(a.id, b.id));

    if (!settingsSection) {
        return nonSettingsSections;
    }

    return [...nonSettingsSections, settingsSection];
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
    const [sections, setSections] = React.useState<ToolboxSection[]>([]);
    const [expandedSectionId, setExpandedSectionId] = React.useState<string>();
    const activeToolChangedCallbacks = React.useRef<
        ((toolId: string) => void)[]
    >([]);
    // The authoritative copy of the sections, so that the adapter methods toolbox.ts
    // calls can read and update the list synchronously. (React state is updated from it,
    // for rendering.)
    const sectionsRef = React.useRef<ToolboxSection[]>([]);

    const applySections = React.useCallback(
        (nextSections: ToolboxSection[]) => {
            sectionsRef.current = nextSections;
            setSections(nextSections);
        },
        [],
    );

    const makeToolActive = React.useCallback((toolId: string) => {
        setExpandedSectionId(toolId);
        activeToolChangedCallbacks.current.forEach((callback) => {
            callback(toolId);
        });
    }, []);

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
                    sectionsRef.current.some((section) => section.id === toolId)
                ) {
                    return;
                }
                applySections(
                    sortSectionsAlphabeticallyWithSettingsLast([
                        ...sectionsRef.current,
                        makeSectionFromToolId(toolId),
                    ]),
                );
            },
            removeTool: (toolId: string) => {
                const remainingSections = sectionsRef.current.filter(
                    (section) => section.id !== toolId,
                );
                if (remainingSections.length === sectionsRef.current.length) {
                    return;
                }
                applySections(remainingSections);
                // Only change which section is expanded if we just removed the expanded
                // one. The awkward functional update guards against a stale value of
                // expandedSectionId.
                setExpandedSectionId((previousExpandedSectionId) =>
                    previousExpandedSectionId === toolId
                        ? remainingSections[0]?.id
                        : previousExpandedSectionId,
                );
            },
            hasTool: (toolId: string) => {
                return sectionsRef.current.some(
                    (section) => section.id === toolId,
                );
            },
            getFirstToolId: () => {
                return sectionsRef.current.find(
                    (section) => section.id !== kSettingsToolId,
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
                        // collapsed section headers, as it did in 6.3 and earlier (BL-16532).
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
                    {sections.map((section) => (
                        <Accordion
                            key={section.id}
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
                            expanded={expandedSectionId === section.id}
                            onChange={(_event, expanded) => {
                                if (expanded) {
                                    makeToolActive(section.id);
                                } else {
                                    setExpandedSectionId(undefined);
                                }
                            }}
                        >
                            <AccordionSummary
                                css={css`
                                    min-height: 32px;
                                    padding-left: 5px;
                                    padding-right: 12px;

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
                                        section.id === kTalkingBookToolId
                                            ? [
                                                  toolboxHeaderIconStyles,
                                                  css`
                                                      width: 12px;
                                                      background-size: 12px 16px;
                                                  `,
                                              ]
                                            : toolboxHeaderIconStyles
                                    }
                                    data-toolid={section.id}
                                    style={
                                        section.iconPath
                                            ? {
                                                  backgroundImage: `url(${section.iconPath})`,
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
                                    <LocalizedString l10nKey={section.l10nKey}>
                                        {section.englishLabel}
                                    </LocalizedString>
                                </Typography>
                                {section.featureName && (
                                    <span>
                                        <SubscriptionBadgeWithTooltipAndDialog
                                            featureName={section.featureName}
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
                                        element={section.toolBodyElement}
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
