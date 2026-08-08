import { FunctionComponent, useSyncExternalStore } from "react";
import { BloomCheckbox } from "../../../react_components/BloomCheckBox";
import {
    getMasterToolList,
    ITool,
    setToolEnabledFromSettings,
} from "../toolbox";
import { getToolboxUiState, subscribeToToolboxUiState } from "../toolboxState";
import { css } from "@emotion/react";
import { SubscriptionBadgeWithTooltipAndDialog } from "../../../react_components/requiresSubscription";
import {
    compareToolsByLabel,
    getToolLabelInfo,
    kSettingsToolId,
} from "../toolIds";

/**
 * The tools the "More..." section offers a checkbox for, in the order it shows them:
 * every registered tool except the ones the user has no say over, that is, the tools that
 * are always enabled (Talking Book), the tools that are only offered on pages that ask for
 * them (Games; see ITool.requiresToolId()), and this "More..." section itself.
 * The order is the same one the toolbox uses for its sections: alphabetical by label.
 */
const getToolsOfferedAsCheckboxes = (): ITool[] =>
    getMasterToolList()
        .filter(
            (tool) =>
                !tool.isAlwaysEnabled() &&
                !tool.requiresToolId() &&
                tool.id() !== kSettingsToolId,
        )
        .sort((a, b) => compareToolsByLabel(a.id(), b.id()));

// One tool's checkbox. Everything except whether it is currently ticked comes from the
// tool itself (see ITool) or is derived from its id (see toolIds.ts).
const ToolboxCheckbox: FunctionComponent<{
    toolId: string;
    // Set only for tools that require a subscription, in which case we show a badge.
    featureName?: string;
    shouldCheck: boolean;
}> = (props) => {
    const labelInfo = getToolLabelInfo(props.toolId);
    return (
        <div
            css={css`
                display: flex;
                align-items: center;
                justify-content: space-between;
                margin-left: 10px;
                margin-right: 10px;
                margin-bottom: -6px;
                padding-top: 0;
            `}
        >
            <BloomCheckbox
                css={css`
                    color: white;
                    border-radius: 0;
                    padding-right: 0;
                    margin-right: 5px;
                    margin-top: 1px;
                `}
                size="small"
                label={labelInfo.englishLabel}
                l10nKey={labelInfo.l10nKey}
                checked={props.shouldCheck}
                onCheckChanged={(checked) => {
                    // Pass true so that, when enabling, the tool opens after a
                    // brief delay letting the user see this checkbox tick before
                    // the "More..." section collapses to reveal the tool. (BL-16501)
                    setToolEnabledFromSettings(props.toolId, checked!, true);
                }}
            />
            {props.featureName && (
                <div
                    css={css`
                        display: inline-flex;
                        padding-top: 10px;
                    `}
                >
                    <SubscriptionBadgeWithTooltipAndDialog
                        featureName={props.featureName}
                    />
                </div>
            )}
        </div>
    );
};

export const SettingsToolControls: FunctionComponent = () => {
    const toolsOffered = getToolsOfferedAsCheckboxes();
    // Which tools are enabled is part of the toolbox's state (see toolboxState.ts), so we
    // just render from it. That way a tool that gets enabled some other way (e.g. clicking
    // a video placeholder to get the Sign Language tool) ticks its checkbox here too.
    const enabledToolIds = useSyncExternalStore(
        subscribeToToolboxUiState,
        getToolboxUiState,
    ).enabledToolIds;

    return (
        <div
            css={css`
                margin-top: 6px;
            `}
        >
            {toolsOffered.map((tool) => (
                <ToolboxCheckbox
                    key={tool.id()}
                    toolId={tool.id()}
                    featureName={tool.featureName}
                    shouldCheck={enabledToolIds.has(tool.id())}
                />
            ))}
        </div>
    );
};
