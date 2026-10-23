// The help topics the old WinForms dialog opened for its tabs, kept for the pages that took over
// those tabs' settings. Every other page has no topic of its own, so it gets the general one.
const kHelpTopicsByPageKey: Record<string, string> = {
    languages: "Tasks/Basic_tasks/Change_languages.htm",
    frontBackMatter:
        "Tasks/Basic_tasks/Select_front_matter_or_back_matter_from_a_pack.htm",
    subscription: "Tasks/Basic_tasks/Enter_Subscription_Code.htm",
};

export const kGeneralSettingsHelpTopic =
    "User_Interface/Dialog_boxes/Settings_dialog_box.htm";

/**
 * The help topic for the Collection Settings page with this pageKey, or the general Settings
 * topic for a page with none (or when no page is showing).
 */
export function collectionSettingsHelpTopic(pageKey: string | undefined) {
    return (
        (pageKey && kHelpTopicsByPageKey[pageKey]) || kGeneralSettingsHelpTopic
    );
}
