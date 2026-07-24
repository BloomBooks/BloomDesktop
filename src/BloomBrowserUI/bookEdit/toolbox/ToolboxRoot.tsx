import * as React from "react";
import { renderRoot } from "../../utils/reactRender";
import axios from "axios";
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
import { setToolboxReactAdapter } from "./toolboxReactAdapter";
import { SubscriptionBadgeWithTooltipAndDialog } from "../../react_components/requiresSubscription";

// React host for the toolbox sidebar. It owns which tools the toolbox is offering, which
// one is expanded, and the DOM node that each tool renders itself into.
//
// Each tool still hands us a plain DOM element (from its ITool.makeRootElement()) rather
// than a React component, so a small host component (ToolBodyHost) puts that element into
// the React layout. When every tool is a React component, each section can render its
// tool directly and both that host and toolboxReactAdapter.ts can go away.

type ToolboxSection = {
    // The tool's id without the historical "Tool" suffix, e.g. "canvas".
    id: string;
    englishLabel: string;
    l10nKey: string;
    // The element the tool renders itself into. Created once, when the section is created.
    toolBodyElement?: HTMLDivElement;
};

// Tools the toolbox offers whether or not the enabledTools API mentions them.
// "settings" is the "More..." section, which is how the user enables the others.
const alwaysOnToolIds: string[] = ["talkingBook", "settings"];

const subscriptionToolIds = new Set<string>(["canvas", "motion", "music"]);

const toolIconPathByToolId: Record<string, string> = {
    talkingBook: "/bloom/images/microphone-white.svg",
    decodableReader: "/bloom/images/keys-white.png",
    leveledReader: "/bloom/images/steps-white.png",
    signLanguage: "/bloom/bookEdit/toolbox/signLanguage/signLanguageTool.svg",
    music: "/bloom/bookEdit/toolbox/music/music-notes-white.svg",
    motion: "/bloom/bookEdit/toolbox/motion/motion.svg",
    canvas: "/bloom/bookEdit/toolbox/canvas/Canvas%20Icon.svg",
    imageDescription:
        "/bloom/bookEdit/toolbox/imageDescription/ImageDescriptionToolIcon.svg",
    impairmentVisualizer:
        "/bloom/bookEdit/toolbox/impairmentVisualizer/blind-eye-white.svg",
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

// Normalize mixed naming conventions (e.g., "canvas" vs "canvasTool") so
// React and legacy code can refer to the same logical tool.
const normalizeToolId = (toolId: string): string => {
    if (!toolId) {
        return toolId;
    }

    if (toolId.endsWith("Tool")) {
        return toolId.substring(0, toolId.length - 4);
    }

    return toolId;
};

// Convert normalized IDs back to the toolbox's traditional "*Tool" names when
// we need to notify the legacy toolbox code.
const toToolboxToolId = (toolId: string): string => {
    if (!toolId) {
        return toolId;
    }
    if (toolId.endsWith("Tool") || toolId.endsWith("Visualizer")) {
        return toolId;
    }
    return `${toolId}Tool`;
};

const getToolboxLabelInfo = (
    toolId: string,
): { englishLabel: string; l10nKey: string } => {
    const normalizedToolId = normalizeToolId(toolId);
    if (normalizedToolId === "settings") {
        return {
            englishLabel: "More...",
            l10nKey: "EditTab.Toolbox.More",
        };
    }

    const toolIdUpper =
        normalizedToolId[0].toUpperCase() +
        normalizedToolId.substring(1, normalizedToolId.length);
    const englishBaseLabel = toolIdUpper.replace(/([A-Z])/g, " $1").trim();
    const endsWithVisualizer = normalizedToolId.endsWith("Visualizer");

    return {
        englishLabel: endsWithVisualizer
            ? englishBaseLabel
            : `${englishBaseLabel} Tool`,
        l10nKey: endsWithVisualizer
            ? `EditTab.Toolbox.${toolIdUpper}`
            : `EditTab.Toolbox.${toolIdUpper}Tool`,
    };
};

// Ask the tool for the element it renders itself into. Returns undefined if we don't know
// about the tool at all, which can happen if settings were saved by a later version of Bloom.
const makeToolBodyElement = (
    normalizedToolId: string,
): HTMLDivElement | undefined => {
    const tool = getMasterToolList().find(
        (candidate) => candidate.id() === normalizedToolId,
    );
    if (!tool) {
        return undefined;
    }

    const toolBodyElement = tool.makeRootElement();
    // Some tool stylesheets still select their body by this attribute.
    toolBodyElement.setAttribute(
        "data-toolid",
        toToolboxToolId(normalizedToolId),
    );
    return toolBodyElement;
};

const makeSectionFromToolId = (toolId: string): ToolboxSection => {
    const normalizedToolId = normalizeToolId(toolId);
    const labelInfo = getToolboxLabelInfo(normalizedToolId);
    return {
        id: normalizedToolId,
        englishLabel: labelInfo.englishLabel,
        l10nKey: labelInfo.l10nKey,
        toolBodyElement: makeToolBodyElement(normalizedToolId),
    };
};

const sortSectionsAlphabeticallyWithSettingsLast = (
    sections: ToolboxSection[],
): ToolboxSection[] => {
    const settingsSection = sections.find(
        (section) => section.id === "settings",
    );
    const nonSettingsSections = sections
        .filter((section) => section.id !== "settings")
        .sort((a, b) =>
            a.englishLabel.localeCompare(b.englishLabel, undefined, {
                sensitivity: "base",
            }),
        );

    if (!settingsSection) {
        return nonSettingsSections;
    }

    return [...nonSettingsSections, settingsSection];
};

const parseEnabledToolIds = (value: string): string[] => {
    const normalized = value
        .split(",")
        .map((toolId) => toolId.trim())
        .filter((toolId) => !!toolId)
        .map((toolId) => normalizeToolId(toolId));

    const toolIds = new Set<string>(normalized);
    alwaysOnToolIds.forEach((toolId) => toolIds.add(toolId));
    return Array.from(toolIds);
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
    // The authoritative copy of the sections, so that the adapter methods the legacy
    // toolbox code calls can read and update the list synchronously. (React state is
    // updated from it, for rendering.)
    const sectionsRef = React.useRef<ToolboxSection[]>([]);

    const applySections = React.useCallback(
        (nextSections: ToolboxSection[]) => {
            sectionsRef.current = nextSections;
            setSections(nextSections);
        },
        [],
    );

    const makeToolActive = React.useCallback((normalizedToolId: string) => {
        setExpandedSectionId(normalizedToolId);
        const toolboxToolId = toToolboxToolId(normalizedToolId);
        activeToolChangedCallbacks.current.forEach((callback) => {
            callback(toolboxToolId);
        });
    }, []);

    // Load the tools the toolbox should offer. (The legacy toolbox code independently
    // announces the same tools through addTool(); whichever gets there first wins, and
    // the other is a no-op.)
    React.useEffect(() => {
        axios
            .get<string>("/bloom/api/toolbox/enabledTools")
            .then((response) => {
                const parsedIds = parseEnabledToolIds(response.data);
                const masterList = getMasterToolList();
                const knownIds = parsedIds.filter((toolId) =>
                    masterList.some((tool) => tool.id() === toolId),
                );
                const existingIds = new Set(
                    sectionsRef.current.map((section) => section.id),
                );
                const newSections = knownIds
                    .filter((toolId) => !existingIds.has(toolId))
                    .map((toolId) => makeSectionFromToolId(toolId));
                applySections(
                    sortSectionsAlphabeticallyWithSettingsLast([
                        ...sectionsRef.current,
                        ...newSections,
                    ]),
                );
            })
            .catch((error) => {
                throw error;
            });
    }, [applySections]);

    // Register the adapter that the legacy toolbox code uses to say which tools the
    // toolbox offers, to make one of them active, and to observe which one is active.
    // See toolboxReactAdapter.ts.
    React.useEffect(() => {
        setToolboxReactAdapter({
            setActiveToolByToolId: (toolId: string) => {
                makeToolActive(normalizeToolId(toolId));
            },
            onActiveToolChanged: (callback: (toolId: string) => void) => {
                activeToolChangedCallbacks.current.push(callback);
            },
            addTool: (toolId: string) => {
                const normalizedToolId = normalizeToolId(toolId);
                if (
                    sectionsRef.current.some(
                        (section) => section.id === normalizedToolId,
                    )
                ) {
                    return;
                }
                applySections(
                    sortSectionsAlphabeticallyWithSettingsLast([
                        ...sectionsRef.current,
                        makeSectionFromToolId(normalizedToolId),
                    ]),
                );
            },
            removeTool: (toolId: string) => {
                const normalizedToolId = normalizeToolId(toolId);
                const remainingSections = sectionsRef.current.filter(
                    (section) => section.id !== normalizedToolId,
                );
                if (remainingSections.length === sectionsRef.current.length) {
                    return;
                }
                applySections(remainingSections);
                // Only change which section is expanded if we just removed the expanded
                // one. The awkward functional update guards against a stale value of
                // expandedSectionId.
                setExpandedSectionId((previousExpandedSectionId) =>
                    previousExpandedSectionId === normalizedToolId
                        ? remainingSections[0]?.id
                        : previousExpandedSectionId,
                );
            },
            hasTool: (toolId: string) => {
                const normalizedToolId = normalizeToolId(toolId);
                return sectionsRef.current.some(
                    (section) => section.id === normalizedToolId,
                );
            },
            getFirstToolId: () => {
                const firstToolSection = sectionsRef.current.find(
                    (section) => section.id !== "settings",
                );
                return firstToolSection
                    ? toToolboxToolId(firstToolSection.id)
                    : undefined;
            },
        });
    }, [applySections, makeToolActive]);

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
                                    css={
                                        section.id === "talkingBook"
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
                                    style={{
                                        backgroundImage: `url(${toolIconPathByToolId[section.id] || ""})`,
                                    }}
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
                                {subscriptionToolIds.has(section.id) && (
                                    <span>
                                        <SubscriptionBadgeWithTooltipAndDialog
                                            featureName={section.id}
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
                                    {section.toolBodyElement ? (
                                        <ToolBodyHost
                                            element={section.toolBodyElement}
                                        />
                                    ) : (
                                        <Typography>
                                            Loading {section.englishLabel}...
                                        </Typography>
                                    )}
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
