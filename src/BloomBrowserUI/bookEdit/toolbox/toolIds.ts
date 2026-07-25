// DO NOT import anything into this file!
// (unless you're sure you know what you're doing)
//
// Note: This file was created to have a short, easy to reference thing
// that wouldn't cause a lot of other things to get dragged into your bundle.
// (Referencing a static variable in the relevant tool would cause a lot of things to get dragged into certain bundles)
// These are also used as feature names when the tool requires a subscription.
//
// This file is also the single place that knows how a tool's canonical id relates to the
// other spellings of it that appear at our boundaries: the "Tool"-suffixed names in
// persisted data and stylesheets, and the English labels and localization keys of the
// toolbox section headers and "More..." checkboxes.
export const kCanvasToolId = "canvas";
export const kGameToolId = "game";
export const kImageDescriptionToolId = "imageDescription";
export const kMotionToolId = "motion";
export const kMusicToolId = "music";
// The "More..." section, which is where the user turns the other tools on and off.
// It is a tool like the others, except that it can never be the "current" tool of a book.
export const kSettingsToolId = "settings";
export const kTalkingBookToolId = "talkingBook";

// Historically, tool names in some contexts carry a "Tool" suffix: the "current" tool and
// the tool "name"s in a book's meta.json, and the data-toolid attribute that some tool
// stylesheets select on. The canonical, in-memory id of a tool is always the unsuffixed
// one that its id() method returns, e.g. "talkingBook". The two functions below are the
// only code that knows about the suffix, and they are called only at those boundaries.
const kPersistedNameSuffix = "Tool";
// One tool id already reads as the name of a tool ("impairmentVisualizer"), so it has
// never taken the suffix.
const kIdEndingThatTakesNoSuffix = "Visualizer";

/**
 * The canonical id (what the tool's id() returns, with no "Tool" suffix) of a tool, given
 * either its canonical id or the "Tool"-suffixed name used in persisted data. Use when
 * reading a tool name that came from outside the toolbox code.
 */
export function toCanonicalToolId(toolIdOrPersistedName: string): string {
    if (toolIdOrPersistedName.endsWith(kPersistedNameSuffix)) {
        return toolIdOrPersistedName.substring(
            0,
            toolIdOrPersistedName.length - kPersistedNameSuffix.length,
        );
    }
    return toolIdOrPersistedName;
}

/**
 * The name to use for a tool where the historical "Tool" suffix is expected: the book's
 * meta.json (the "current" tool and the enabled-tool names) and the data-toolid attribute
 * of a tool's body element. Given a canonical tool id, appends the suffix, except to ids
 * that never took it.
 */
export function toPersistedToolName(toolId: string): string {
    if (
        !toolId ||
        toolId.endsWith(kPersistedNameSuffix) ||
        toolId.endsWith(kIdEndingThatTakesNoSuffix)
    ) {
        return toolId;
    }
    return toolId + kPersistedNameSuffix;
}

// The other historical spelling: the editView/saveToolboxSetting API identifies the
// enabled/disabled setting of a tool by the id of the checkbox that used to control it,
// which was the tool's id followed by "Check". The C# side strips the suffix off again
// (see ToolboxView.SaveToolboxSettings), so what lands in the book's meta.json is the
// canonical tool id.
const kEnabledSettingNameSuffix = "Check";

/**
 * The name that editView/saveToolboxSetting expects for the setting that says whether a
 * tool is enabled in this book. Given a canonical tool id.
 */
export function toEnabledSettingName(toolId: string): string {
    return toolId + kEnabledSettingNameSuffix;
}

/**
 * The English label and localization key of a tool, used both for its toolbox section
 * header and for its checkbox in the "More..." section. Both are derived from the
 * canonical tool id: "decodableReader" gives "Decodable Reader Tool" and
 * "EditTab.Toolbox.DecodableReaderTool".
 */
export function getToolLabelInfo(toolId: string): {
    englishLabel: string;
    l10nKey: string;
} {
    if (toolId === kSettingsToolId) {
        return {
            englishLabel: "More...",
            l10nKey: "EditTab.Toolbox.More",
        };
    }

    const capitalizedId = toolId[0].toUpperCase() + toolId.substring(1);
    const spacedLabel = capitalizedId.replace(/([A-Z])/g, " $1").trim();
    // An id ending in "Visualizer" already reads as the name of a tool, so neither the
    // label nor the key gets "Tool" added (the same rule as toPersistedToolName).
    if (capitalizedId.endsWith(kIdEndingThatTakesNoSuffix)) {
        return {
            englishLabel: spacedLabel,
            l10nKey: `EditTab.Toolbox.${capitalizedId}`,
        };
    }
    return {
        englishLabel: `${spacedLabel} Tool`,
        l10nKey: `EditTab.Toolbox.${capitalizedId}Tool`,
    };
}

/**
 * Orders two tools the way the toolbox presents them: alphabetically by English label.
 * (The toolbox itself puts the "More..." section last, whatever this says about it.)
 */
export function compareToolsByLabel(toolIdA: string, toolIdB: string): number {
    return getToolLabelInfo(toolIdA).englishLabel.localeCompare(
        getToolLabelInfo(toolIdB).englishLabel,
        undefined,
        { sensitivity: "base" },
    );
}
