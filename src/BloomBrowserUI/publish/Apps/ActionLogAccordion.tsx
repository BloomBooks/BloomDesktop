import { css } from "@emotion/react";
import ContentCopyIcon from "@mui/icons-material/ContentCopy";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import {
    Accordion,
    AccordionDetails,
    AccordionSummary,
    IconButton,
} from "@mui/material";
import * as React from "react";
import { BloomTooltip } from "../../react_components/BloomToolTip";
import { ProgressBox } from "../../react_components/Progress/progressBox";
import type { ProgressBoxHandle } from "../../react_components/Progress/progressBox";
import { useL10n } from "../../react_components/l10nHooks";
import { postJson } from "../../utils/bloomApi";

export interface IActionLogController {
    progressBoxRef: React.RefObject<ProgressBoxHandle | null>;
    messages: Array<JSX.Element>;
    setMessages: React.Dispatch<React.SetStateAction<JSX.Element[]>>;
    rawMessages: string[];
    isExpanded: boolean;
    setIsExpanded: React.Dispatch<React.SetStateAction<boolean>>;
    hasMessages: boolean;
    appendRawMessage: (message: string) => void;
    clear: () => void;
    openForError: () => void;
    scrollToLastError: () => void;
}

export function useActionLogController(): IActionLogController {
    const progressBoxRef = React.useRef<ProgressBoxHandle>(null);
    const [messages, setMessages] = React.useState<Array<JSX.Element>>([]);
    const [rawMessages, setRawMessages] = React.useState<string[]>([]);
    const [isExpanded, setIsExpanded] = React.useState(false);
    const [shouldScrollToError, setShouldScrollToError] = React.useState(false);
    const hasMessages = messages.length > 0;

    const appendRawMessage = React.useCallback(function appendRawMessage(
        message: string,
    ) {
        setRawMessages((old) => [...old, message]);
    }, []);

    const clear = React.useCallback(() => {
        setMessages([]);
        setRawMessages([]);
        setIsExpanded(false);
        setShouldScrollToError(false);
    }, []);

    const openForError = React.useCallback(() => {
        setIsExpanded(true);
        setShouldScrollToError(true);
    }, []);

    const scrollToLastError = React.useCallback(() => {
        progressBoxRef.current?.scrollToLastError();
    }, []);

    // When an error opens the accordion, wait until the new render has committed before scrolling inside the log.
    React.useEffect(() => {
        if (!hasMessages || !isExpanded || !shouldScrollToError) {
            return;
        }

        const timeoutId = window.setTimeout(() => {
            scrollToLastError();
            setShouldScrollToError(false);
        }, 0);

        return () => {
            window.clearTimeout(timeoutId);
        };
    }, [hasMessages, isExpanded, scrollToLastError, shouldScrollToError]);

    return {
        progressBoxRef,
        messages,
        setMessages,
        rawMessages,
        isExpanded,
        setIsExpanded,
        hasMessages,
        appendRawMessage,
        clear,
        openForError,
        scrollToLastError,
    };
}

export const ActionLogAccordion: React.FunctionComponent<{
    controller: IActionLogController;
    isActive: boolean;
    webSocketContext?: string;
    dataTestId?: string;
}> = (props) => {
    const copyTooltip = useL10n("Copy", "Common.Copy");
    const logTextContainerRef = React.useRef<HTMLDivElement>(null);

    // Keep the ProgressBox mounted while an action is running so the first websocket lines are captured
    // even before there is enough content to show the accordion.
    const shouldRenderListener = props.isActive || props.controller.hasMessages;

    const copyLogToClipboard = React.useCallback(() => {
        const rawLogText = props.controller.rawMessages.join("\n");
        const renderedLogText =
            logTextContainerRef.current?.innerText?.trim() ?? "";

        postJson("common/clipboardText", {
            text: rawLogText || renderedLogText,
        });
    }, [props.controller.rawMessages]);

    if (!shouldRenderListener) {
        return null;
    }

    const progressBox = (
        <ProgressBox
            ref={props.controller.progressBoxRef}
            webSocketContext={props.webSocketContext}
            messages={props.controller.messages}
            setMessages={props.controller.setMessages}
            onMessageLogged={props.controller.appendRawMessage}
            onGotErrorMessage={props.controller.openForError}
            css={css`
                height: 100%;
                min-height: 0;
            `}
        />
    );

    if (!props.controller.hasMessages) {
        return (
            <div
                aria-hidden={true}
                data-testid={
                    props.dataTestId
                        ? `${props.dataTestId}-listener`
                        : undefined
                }
                css={css`
                    position: absolute;
                    width: 0;
                    height: 0;
                    overflow: hidden;
                    pointer-events: none;
                    opacity: 0;
                `}
            >
                {progressBox}
            </div>
        );
    }

    return (
        <Accordion
            data-testid={props.dataTestId}
            disableGutters
            expanded={props.controller.isExpanded}
            onChange={(_event, expanded) =>
                props.controller.setIsExpanded(expanded)
            }
            css={css`
                margin-top: 8px;
                margin-bottom: 0;
                border: 0;
                border-radius: 0;
                box-shadow: none;
                overflow: visible;
                background: transparent;

                &:before {
                    display: none;
                }
            `}
        >
            <AccordionSummary
                data-testid={
                    props.dataTestId ? `${props.dataTestId}-summary` : undefined
                }
                css={css`
                    min-height: 24px;
                    padding: 0;
                    justify-content: flex-start;
                    text-align: left;

                    & .MuiAccordionSummary-content {
                        margin: 0;
                        font-weight: 600;
                        flex-grow: 0;
                        justify-content: flex-start;
                    }

                    &.Mui-expanded {
                        min-height: 24px;
                    }

                    &.Mui-expanded .action-log-chevron {
                        transform: rotate(90deg);
                    }
                `}
            >
                <span
                    css={css`
                        display: inline-flex;
                        align-items: center;
                        gap: 4px;
                    `}
                >
                    <span>Log</span>
                    <ExpandMoreIcon
                        className="action-log-chevron"
                        css={css`
                            font-size: 18px;
                            transform: rotate(-90deg);
                            transform-origin: center;
                            transition: transform 150ms ease;
                        `}
                    />
                </span>
            </AccordionSummary>
            <AccordionDetails
                css={css`
                    padding: 4px 16px 0;
                `}
            >
                <div
                    css={css`
                        display: flex;
                        justify-content: flex-end;
                        margin-bottom: 4px;
                    `}
                >
                    <BloomTooltip tip={copyTooltip}>
                        <IconButton
                            aria-label={copyTooltip}
                            onClick={copyLogToClipboard}
                            size="small"
                            data-testid={
                                props.dataTestId
                                    ? `${props.dataTestId}-copy-button`
                                    : undefined
                            }
                        >
                            <ContentCopyIcon fontSize="small" />
                        </IconButton>
                    </BloomTooltip>
                </div>
                <div
                    ref={logTextContainerRef}
                    css={css`
                        position: relative;
                        min-height: 8em;
                        height: 8em;
                        width: auto;
                        max-width: 900px;
                        resize: vertical;
                        overflow: hidden;
                        border: 1px solid #c9c9c9;
                        border-radius: 4px;
                        background: #fff;
                        box-sizing: border-box;

                        &::after {
                            content: "◢";
                            position: absolute;
                            right: 4px;
                            bottom: 0;
                            color: #8a8a8a;
                            font-size: 12px;
                            line-height: 1;
                            pointer-events: none;
                        }
                    `}
                >
                    {progressBox}
                </div>
            </AccordionDetails>
        </Accordion>
    );
};
