import { css } from "@emotion/react";
import { ConfigrGroup, ConfigrPage, ConfigrStatic } from "@sillsdev/config-r";
import { useL10n } from "../../react_components/l10nHooks";
import { ISettingsPage } from "./settingsPage";

// Temporary content for every page. Each of the seven tab cards replaces its page's group with
// real controls, so this text is deliberately plain English and is never localized.
const PagePlaceholder: React.FunctionComponent = () => (
    <div
        css={css`
            font-size: 0.9em;
            color: #555;
        `}
    >
        Settings for this section are not available yet.
    </div>
);

/**
 * A page of the Collection Settings dialog that has no settings yet. C# names its pageKey when it
 * asks the dialog to open on a particular page; it moves to a page of its own when its tab card
 * adds it.
 */
export function usePlaceholderPage(
    pageKey: string,
    englishLabel: string,
    labelL10nKey: string,
    helpTopic?: string,
): ISettingsPage {
    const label = useL10n(englishLabel, labelL10nKey);
    return {
        element: (
            <ConfigrPage label={label} pageKey={pageKey} topLevel={true}>
                <ConfigrGroup label={label}>
                    <ConfigrStatic>
                        <PagePlaceholder />
                    </ConfigrStatic>
                </ConfigrGroup>
            </ConfigrPage>
        ),
        helpTopic,
    };
}
