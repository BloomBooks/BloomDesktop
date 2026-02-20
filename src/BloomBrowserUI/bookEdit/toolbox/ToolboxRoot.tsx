import * as React from "react";
import * as ReactDOM from "react-dom";
import axios from "axios";
import { css } from "@emotion/react";
import Accordion from "@mui/material/Accordion";
import AccordionDetails from "@mui/material/AccordionDetails";
import AccordionSummary from "@mui/material/AccordionSummary";
import Typography from "@mui/material/Typography";
import { ThemeProvider } from "@mui/material/styles";
import { toolboxTheme } from "../../bloomMaterialUITheme";
import {
    kBloomBlue,
    kBloomPanelBackground,
    kBloomUnselectedTabBackground,
} from "../../utils/colorUtils";

type ToolboxReactAdapter = {
    isEnabled: () => boolean;
    setActiveToolByToolId: (toolId: string) => void;
    getActiveToolId: () => string | undefined;
    onActiveToolChanged: (callback: (toolId: string) => void) => void;
};

type IToolboxTool = {
    makeRootElement?: () => HTMLDivElement;
};

type ICurrentTool = {
    id: () => string;
};

type IToolboxBundle = {
    getTheOneToolbox?: () => {
        getToolIfOffered?: (toolId: string) => IToolboxTool | undefined;
        getCurrentTool?: () => ICurrentTool | undefined;
    };
};

declare global {
    interface Window {
        toolboxReactAdapter?: ToolboxReactAdapter;
        toolboxBundle?: IToolboxBundle;
    }
}

type ToolboxSection = {
    id: string;
    label: string;
    body: string;
    order: number;
    legacyToolHtmlSubPath?: string;
    legacyToolBodyHtml?: string;
    liveToolBodyElement?: HTMLDivElement;
};

const alwaysOnToolIds: string[] = ["talkingBook"];

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

const legacyToolSubPathByToolId: Record<string, string> = {
    talkingBook: "talkingBook/talkingBookToolboxTool.html",
    decodableReader: "readers/decodableReader/decodableReaderToolboxTool.html",
    leveledReader: "readers/leveledReader/leveledReaderToolboxTool.html",
    settings: "settings/Settings.html",
    settingsTool: "settings/Settings.html",
};

const normalizeToolId = (toolId: string): string => {
    if (!toolId) {
        return toolId;
    }

    if (toolId.endsWith("Tool")) {
        return toolId.substring(0, toolId.length - 4);
    }

    return toolId;
};

const toToolboxToolId = (toolId: string): string => {
    if (!toolId) {
        return toolId;
    }
    if (toolId.endsWith("Tool") || toolId.endsWith("Visualizer")) {
        return toolId;
    }
    return `${toolId}Tool`;
};

const makeLabelFromToolId = (toolId: string): string => {
    if (toolId === "settings") {
        return "More...";
    }

    return toolId
        .replace(/Tool$/, "")
        .replace(/([A-Z])/g, " $1")
        .trim()
        .replace(/^./, (c) => c.toUpperCase());
};

const sortToolIdsAlphabetically = (toolIds: string[]): string[] => {
    return [...toolIds].sort((a, b) =>
        makeLabelFromToolId(a).localeCompare(
            makeLabelFromToolId(b),
            undefined,
            {
                sensitivity: "base",
            },
        ),
    );
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

const buildSectionsFromEnabledToolIds = (
    enabledToolIds: string[],
): ToolboxSection[] => {
    const withMoreTabLast = sortToolIdsAlphabetically(
        enabledToolIds.filter((id) => id !== "settings"),
    );
    withMoreTabLast.push("settings");

    return withMoreTabLast.map((toolId, index) => {
        const label = makeLabelFromToolId(toolId);
        return {
            id: toolId,
            label,
            body: `${label} React panel scaffold (legacy tool body integration comes in Slice 3).`,
            order: index,
            legacyToolHtmlSubPath: legacyToolSubPathByToolId[toolId],
        };
    });
};

const getExistingToolboxContentElement = (
    toolId: string,
): HTMLDivElement | undefined => {
    const normalizedToolId = normalizeToolId(toolId);
    const candidateToolIds = new Set<string>([
        toolId,
        normalizedToolId,
        toToolboxToolId(toolId),
        toToolboxToolId(normalizedToolId),
    ]);

    for (const candidateToolId of candidateToolIds) {
        const contentElement = document.querySelector(
            `#toolbox > div[data-toolid='${candidateToolId}'], #toolbox > div[data-toolId='${candidateToolId}']`,
        ) as HTMLDivElement | null;
        if (contentElement) {
            return contentElement;
        }
    }

    return undefined;
};

const getLiveToolBodyElement = (toolId: string): HTMLDivElement | undefined => {
    // Use the element created by legacy beginAddTool() so we don't instantiate
    // a second React tool root and desynchronize tool state.
    return getExistingToolboxContentElement(toolId);
};

const getLegacyCurrentToolId = (): string | undefined => {
    const toolbox = window.toolboxBundle?.getTheOneToolbox?.();
    const currentTool = toolbox?.getCurrentTool?.();
    const currentToolId = currentTool?.id?.();
    if (!currentToolId) {
        return undefined;
    }

    return normalizeToolId(currentToolId);
};

const LiveToolBodyHost: React.FunctionComponent<{ element: HTMLDivElement }> = (
    props,
) => {
    const hostRef = React.useRef<HTMLDivElement | null>(null);

    const clearLegacyAccordionSizing = React.useCallback(
        (element: HTMLDivElement) => {
            element.style.height = "100%";
            element.style.width = "100%";
            element.style.minWidth = "0";
            element.style.flex = "1 1 auto";
            element.style.overflow = "";
            element.style.display = "block";
        },
        [],
    );

    React.useEffect(() => {
        const host = hostRef.current;
        if (!host) {
            return;
        }

        clearLegacyAccordionSizing(props.element);

        if (!host.contains(props.element)) {
            host.innerHTML = "";
            host.appendChild(props.element);
        }

        return () => {
            if (host.contains(props.element)) {
                host.removeChild(props.element);
            }
        };
    }, [props.element, clearLegacyAccordionSizing]);

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
            `}
        ></div>
    );
};

const extractFirstToolContentDivAsHtml = (rawHtml: string): string => {
    const parsedDocument = new DOMParser().parseFromString(
        rawHtml,
        "text/html",
    );
    const topLevelElements = Array.from(parsedDocument.body.children);
    const firstTopLevelDiv = topLevelElements.find(
        (element) => element.tagName.toLowerCase() === "div",
    );
    if (firstTopLevelDiv) {
        return firstTopLevelDiv.outerHTML;
    }

    const firstAnyDiv = parsedDocument.body.querySelector("div");
    if (firstAnyDiv) {
        return firstAnyDiv.outerHTML;
    }

    throw new Error("Legacy toolbox html did not contain a div tool body.");
};

const loadLegacyToolBodyHtml = async (
    section: ToolboxSection,
): Promise<string | undefined> => {
    if (!section.legacyToolHtmlSubPath) {
        return undefined;
    }

    const response = await axios.get<string>(
        `/bloom/bookEdit/toolbox/${section.legacyToolHtmlSubPath}`,
    );
    return extractFirstToolContentDivAsHtml(response.data);
};

export const ToolboxRoot: React.FunctionComponent = () => {
    const [sections, setSections] = React.useState<ToolboxSection[]>([]);
    const [expandedSectionId, setExpandedSectionId] = React.useState<string>();
    const activeToolChangedCallbacks = React.useRef<
        ((toolId: string) => void)[]
    >([]);
    const hydratedToolIds = React.useRef<Set<string>>(new Set());

    const hydrateToolBody = React.useCallback(async (toolId: string) => {
        if (hydratedToolIds.current.has(toolId)) {
            return;
        }

        const liveToolBodyElement = getLiveToolBodyElement(toolId);
        if (liveToolBodyElement) {
            hydratedToolIds.current.add(toolId);
            setSections((previousSections) =>
                previousSections.map((section) =>
                    section.id === toolId
                        ? {
                              ...section,
                              liveToolBodyElement,
                          }
                        : section,
                ),
            );
            return;
        }

        const legacyToolHtmlSubPath = legacyToolSubPathByToolId[toolId];
        if (legacyToolHtmlSubPath) {
            try {
                const legacyToolBodyHtml = await loadLegacyToolBodyHtml({
                    id: toolId,
                    label: "",
                    body: "",
                    order: 0,
                    legacyToolHtmlSubPath,
                });
                hydratedToolIds.current.add(toolId);
                setSections((previousSections) =>
                    previousSections.map((section) =>
                        section.id === toolId
                            ? {
                                  ...section,
                                  legacyToolBodyHtml,
                              }
                            : section,
                    ),
                );
            } catch (error) {
                console.error(
                    `Failed to load legacy toolbox HTML for ${toolId}.`,
                    error,
                );
            }
            return;
        }
    }, []);

    // Load enabled toolbox tools so Slice 2 can render real section metadata in React.
    React.useEffect(() => {
        axios
            .get<string>("/bloom/api/toolbox/enabledTools")
            .then(async (response) => {
                const parsedIds = parseEnabledToolIds(response.data);
                const builtSections =
                    buildSectionsFromEnabledToolIds(parsedIds);
                setSections(builtSections);
                builtSections.forEach((section) => {
                    void hydrateToolBody(section.id);
                });
            })
            .catch((error) => {
                throw error;
            });
    }, [hydrateToolBody]);

    // Some tool content elements may appear shortly after we build sections.
    // Keep trying unresolved live tools until their existing DOM is available.
    React.useEffect(() => {
        const sectionsToUpgrade = sections
            .filter(
                (section) =>
                    !!section.legacyToolBodyHtml &&
                    !section.liveToolBodyElement &&
                    !!getLiveToolBodyElement(section.id),
            )
            .map((section) => section.id);

        if (sectionsToUpgrade.length === 0) {
            return;
        }

        setSections((previousSections) =>
            previousSections.map((section) => {
                if (!sectionsToUpgrade.includes(section.id)) {
                    return section;
                }

                const liveToolBodyElement = getLiveToolBodyElement(section.id);
                if (!liveToolBodyElement) {
                    return section;
                }

                hydratedToolIds.current.add(section.id);
                return {
                    ...section,
                    liveToolBodyElement,
                    legacyToolBodyHtml: undefined,
                };
            }),
        );
    }, [sections]);

    React.useEffect(() => {
        const disconnectedToolIds = sections
            .filter(
                (section) =>
                    !!section.liveToolBodyElement &&
                    !section.liveToolBodyElement.isConnected,
            )
            .map((section) => section.id);

        if (disconnectedToolIds.length === 0) {
            return;
        }

        disconnectedToolIds.forEach((toolId) => {
            hydratedToolIds.current.delete(toolId);
        });

        setSections((previousSections) =>
            previousSections.map((section) =>
                disconnectedToolIds.includes(section.id)
                    ? {
                          ...section,
                          liveToolBodyElement: undefined,
                      }
                    : section,
            ),
        );
    }, [sections]);

    React.useEffect(() => {
        const intervalId = window.setInterval(() => {
            sections.forEach((section) => {
                const hasBodyContent =
                    !!section.legacyToolBodyHtml ||
                    !!section.liveToolBodyElement;

                if (hasBodyContent) {
                    return;
                }

                if (
                    section.id === expandedSectionId &&
                    hydratedToolIds.current.has(section.id)
                ) {
                    hydratedToolIds.current.delete(section.id);
                }

                if (!hydratedToolIds.current.has(section.id)) {
                    void hydrateToolBody(section.id);
                }
            });
        }, 250);

        return () => {
            window.clearInterval(intervalId);
        };
    }, [sections, hydrateToolBody, expandedSectionId]);

    React.useEffect(() => {
        const onToolAdded = (event: Event) => {
            const customEvent = event as CustomEvent<{ toolId: string }>;
            const addedToolId = normalizeToolId(customEvent.detail.toolId);

            setSections((previousSections) => {
                if (
                    previousSections.some(
                        (section) => section.id === addedToolId,
                    )
                ) {
                    return previousSections;
                }

                const settingsSection = previousSections.find(
                    (section) => section.id === "settings",
                );
                const nonSettingsSections = previousSections
                    .filter((section) => section.id !== "settings")
                    .sort((a, b) =>
                        a.label.localeCompare(b.label, undefined, {
                            sensitivity: "base",
                        }),
                    );
                const addedSection = {
                    id: addedToolId,
                    label: makeLabelFromToolId(addedToolId),
                    body: `${makeLabelFromToolId(addedToolId)} React panel scaffold (legacy tool body integration comes in Slice 3).`,
                    order: nonSettingsSections.length,
                    legacyToolHtmlSubPath:
                        legacyToolSubPathByToolId[addedToolId],
                } as ToolboxSection;

                const nextSections = [
                    ...nonSettingsSections,
                    addedSection,
                ].sort((a, b) =>
                    a.label.localeCompare(b.label, undefined, {
                        sensitivity: "base",
                    }),
                );
                if (settingsSection) {
                    nextSections.push(settingsSection);
                }
                return nextSections;
            });

            void hydrateToolBody(addedToolId);
        };

        const onToolRemoved = (event: Event) => {
            const customEvent = event as CustomEvent<{ toolId: string }>;
            const removedToolId = normalizeToolId(customEvent.detail.toolId);
            hydratedToolIds.current.delete(removedToolId);

            setSections((previousSections) => {
                const nextSections = previousSections.filter(
                    (section) => section.id !== removedToolId,
                );

                if (expandedSectionId === removedToolId) {
                    const firstSection = nextSections[0];
                    setExpandedSectionId(
                        firstSection ? firstSection.id : undefined,
                    );
                }

                return nextSections;
            });
        };

        window.addEventListener("toolbox-tool-added", onToolAdded);
        window.addEventListener("toolbox-tool-removed", onToolRemoved);

        return () => {
            window.removeEventListener("toolbox-tool-added", onToolAdded);
            window.removeEventListener("toolbox-tool-removed", onToolRemoved);
        };
    }, [expandedSectionId, hydrateToolBody]);

    // Expose activation adapter so legacy toolbox code can drive and observe React accordion state.
    React.useEffect(() => {
        if (!expandedSectionId) {
            const legacyCurrentToolId = getLegacyCurrentToolId();
            if (
                legacyCurrentToolId &&
                sections.some((section) => section.id === legacyCurrentToolId)
            ) {
                setExpandedSectionId(legacyCurrentToolId);
            }
        }

        window.toolboxReactAdapter = {
            isEnabled: () => true,
            setActiveToolByToolId: (toolId: string) => {
                setExpandedSectionId(normalizeToolId(toolId));
            },
            getActiveToolId: () => {
                if (!expandedSectionId) {
                    return undefined;
                }
                return toToolboxToolId(expandedSectionId);
            },
            onActiveToolChanged: (callback: (toolId: string) => void) => {
                activeToolChangedCallbacks.current.push(callback);
            },
        };
    }, [expandedSectionId, sections]);

    // The old jQuery toolbox logic still runs for now, and it calls .show() on #toolbox.
    // Keep that legacy root hidden so only the React root is visible.
    React.useEffect(() => {
        const legacyToolboxElement = document.getElementById("toolbox");
        if (!legacyToolboxElement) {
            return;
        }

        const forceHideLegacyToolbox = () => {
            legacyToolboxElement.style.setProperty(
                "display",
                "none",
                "important",
            );
        };

        forceHideLegacyToolbox();

        const observer = new MutationObserver(() => {
            forceHideLegacyToolbox();
        });
        observer.observe(legacyToolboxElement, {
            attributes: true,
            attributeFilter: ["style", "class"],
        });

        return () => {
            observer.disconnect();
        };
    }, []);

    return (
        <div
            css={css`
                height: 100%;
                display: flex;
                flex-direction: column;
            `}
        >
            <ThemeProvider theme={toolboxTheme}>
                <div
                    css={css`
                        border-bottom: 1px solid rgba(255, 255, 255, 0.2);
                        background-color: ${kBloomPanelBackground};
                        display: flex;
                        flex-direction: column;
                        height: 100%;
                        min-height: 0;

                        a {
                            color: white;
                        }

                        .helpLinkWrapper a {
                            color: white;
                        }

                        .toolbox-main-accordion {
                            background-color: ${kBloomUnselectedTabBackground};
                            color: white;
                            margin: 0;
                            display: flex;
                            flex-direction: column;
                            flex-shrink: 0;
                            &:before {
                                display: none;
                            }
                        }

                        .toolbox-main-accordion.Mui-expanded {
                            background-color: ${kBloomPanelBackground};
                            flex: 1 1 auto;
                            min-height: 0;
                        }

                        .toolbox-main-accordion.Mui-expanded
                            > .MuiCollapse-root {
                            display: flex;
                            flex-direction: column;
                            flex: 1;
                            min-height: 0;
                            overflow: hidden;
                        }

                        .toolbox-main-accordion.Mui-expanded
                            > .MuiCollapse-root
                            > .MuiCollapse-wrapper,
                        .toolbox-main-accordion.Mui-expanded
                            > .MuiCollapse-root
                            > .MuiCollapse-wrapper
                            > .MuiCollapse-wrapperInner,
                        .toolbox-main-accordion.Mui-expanded
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

                        .toolbox-main-summary {
                            min-height: 32px;
                            padding-left: 5px;
                            padding-right: 12px;
                        }

                        .toolbox-main-summary .MuiAccordionSummary-content {
                            margin: 8px 0;
                            display: flex;
                            align-items: center;
                            gap: 12px;
                        }

                        .toolbox-react-header-text {
                            flex-grow: 1;
                        }

                        .toolbox-react-header-icon {
                            width: 16px;
                            height: 16px;
                            display: inline-block;
                            background-position: center;
                            background-repeat: no-repeat;
                            background-size: contain;
                            flex-shrink: 0;
                        }

                        .toolbox-react-header-icon[data-toolid="talkingBook"] {
                            width: 12px;
                            background-size: 12px 16px;
                        }

                        .toolbox-main-summary.Mui-expanded {
                            min-height: 32px;
                            background-color: ${kBloomBlue};
                        }

                        .toolbox-main-details {
                            background-color: ${kBloomPanelBackground};
                            padding: 0;
                            flex: 1;
                            display: flex;
                            min-height: 0;
                            overflow: auto;
                        }

                        .toolbox-react-content {
                            width: 100%;
                            display: flex;
                            flex-direction: column;
                            align-items: stretch;
                            min-height: 100%;
                            overflow: visible;
                        }

                        .toolbox-react-content #leveled-reader-tool-content,
                        .toolbox-react-content #decodable-reader-tool-content {
                            width: 100% !important;
                            box-sizing: border-box;
                            min-width: 0;
                            display: block !important;
                            align-self: stretch;
                            margin-right: 0 !important;
                            padding-right: 0 !important;
                        }

                        .toolbox-react-content
                            > div[data-toolid="leveledReaderTool"],
                        .toolbox-react-content
                            > div[data-toolid="decodableReaderTool"],
                        .toolbox-react-content
                            > div[data-toolId="leveledReaderTool"],
                        .toolbox-react-content
                            > div[data-toolId="decodableReaderTool"] {
                            width: 100% !important;
                            min-width: 0;
                            align-self: stretch !important;
                            display: block !important;
                            box-sizing: border-box;
                            margin-right: 0 !important;
                            padding-right: 0 !important;
                        }
                    `}
                >
                    {sections.map((section) => (
                        <Accordion
                            key={section.id}
                            className="toolbox-main-accordion"
                            disableGutters
                            expanded={expandedSectionId === section.id}
                            onChange={(_event, expanded) => {
                                const nextSectionId = expanded
                                    ? section.id
                                    : undefined;
                                setExpandedSectionId(nextSectionId);
                                if (nextSectionId) {
                                    const toolId =
                                        toToolboxToolId(nextSectionId);
                                    activeToolChangedCallbacks.current.forEach(
                                        (callback) => {
                                            callback(toolId);
                                        },
                                    );
                                }
                            }}
                        >
                            <AccordionSummary className="toolbox-main-summary">
                                <span
                                    className="toolbox-react-header-icon"
                                    data-toolid={section.id}
                                    style={{
                                        backgroundImage: `url(${toolIconPathByToolId[section.id] || ""})`,
                                    }}
                                ></span>
                                <Typography className="toolbox-react-header-text">
                                    {section.label}
                                </Typography>
                                {subscriptionToolIds.has(section.id) && (
                                    <span className="subscription-badge"></span>
                                )}
                            </AccordionSummary>
                            <AccordionDetails className="toolbox-main-details">
                                <div className="toolbox-react-content">
                                    {section.liveToolBodyElement ? (
                                        <LiveToolBodyHost
                                            element={
                                                section.liveToolBodyElement
                                            }
                                        />
                                    ) : section.legacyToolBodyHtml ? (
                                        <div
                                            css={css`
                                                width: 100%;
                                                min-height: 100%;
                                                display: flex;
                                                flex-direction: column;
                                            `}
                                            dangerouslySetInnerHTML={{
                                                __html: section.legacyToolBodyHtml,
                                            }}
                                        ></div>
                                    ) : (
                                        <Typography>{section.body}</Typography>
                                    )}
                                </div>
                            </AccordionDetails>
                        </Accordion>
                    ))}
                </div>
            </ThemeProvider>
            <div
                id="toolbox"
                css={css`
                    display: none;
                `}
            ></div>
        </div>
    );
};

export const renderToolboxRoot = (): void => {
    const hostElement = document.getElementById("toolbox-react-root");
    if (!hostElement) {
        return;
    }

    hostElement.style.height = "100%";
    hostElement.style.display = "flex";
    hostElement.style.flexDirection = "column";

    ReactDOM.render(<ToolboxRoot />, hostElement);
};
