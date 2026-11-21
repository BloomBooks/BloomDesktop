import { css } from "@emotion/react";
import { ConfigrBoolean } from "@sillsdev/config-r";
import * as React from "react";
import { useApiObject } from "../../utils/bloomApi";
import { useL10n } from "../../react_components/l10nHooks";
import { useMemo } from "react";

export interface ILanguageNameValues {
    language1Name: string;
    language1Tag: string;
    language2Name: string;
    language2Tag: string;
    language3Name?: string;
    language3Tag?: string;
}

/** The written languages' names, and the label of each language's checkbox in a FieldVisibilityGroup. */
export interface IFieldVisibilityLabels {
    languageNames: ILanguageNameValues;
    L1: string;
    L2: string;
    L3: string;
}

/**
 * The labels of the checkboxes a FieldVisibilityGroup shows, made from a frame such as
 * "Show Title in {0}" and each written language's name. Its caller passes them to the group, and
 * to Config-R as the group's searchTerms, since Config-R's search cannot see what the group renders.
 */
export function useFieldVisibilityLabels(
    labelFrame: string,
    labelFrameL10nKey: string,
): IFieldVisibilityLabels {
    // Enhance: we may decide to let the dialog retrieve these and pass them in, to reduce API calls,
    // if we actually start having more than one field configured with this component.
    const languageNames: ILanguageNameValues =
        useApiObject<ILanguageNameValues>("settings/languageNames", {
            language1Name: "",
            language1Tag: "",
            language2Name: "",
            language2Tag: "",
        });
    const L1 = useL10n(
        labelFrame,
        labelFrameL10nKey,
        "",
        languageNames.language1Name,
    );
    const L2 = useL10n(
        labelFrame,
        labelFrameL10nKey,
        "",
        languageNames.language2Name,
    );
    const L3 = useL10n(
        labelFrame,
        labelFrameL10nKey,
        undefined,
        languageNames.language3Name,
    );
    return { languageNames, L1, L2, L3 };
}

// This is used in BookSettingsDialog to provide a group of Config-R booleans that control
// which languages to show for a specified field.
// It is currently used for the cover title languages.
// It controls values for three Config-R booleans, called <field>-L1-show, <field>-L2-show, and <field>-L3-show.
export const FieldVisibilityGroup: React.FunctionComponent<{
    field: string; // the field we're configuring, e.g. "cover-title", without the "-LN-show" suffix
    // From useFieldVisibilityLabels.
    labels: IFieldVisibilityLabels;
    // Read by Config-R's search, not here: the labels, so a search for one finds this group.
    searchTerms: string;
    settings: object | undefined;
    settingsToReturnLater: object | undefined;
    disabled: boolean;
    L1MustBeTurnedOn?: boolean;

    // I wish this function just lived here, or could be imported, but it is used for other
    // booleans in the parent dialog, so we can't just move it, and it uses React state from the dialog,
    // so we can't just import it. And it does complex and important work, especially to
    // support allowing xmatter and branding to lock certain settings. So the only way I could
    // make it work and let that functionality be shared was to pass it in.
    getAdditionalProps: (subPath: string) => {
        path: string;
        overrideValue: boolean;
        overrideDescription?: string;
    };
}> = (props) => {
    const languageNameValues = props.labels.languageNames;

    const L1Field = `${props.field}-L1-show`;
    const L2Field = `${props.field}-L2-show`;
    const L3Field = `${props.field}-L3-show`;

    const showL2Control =
        languageNameValues.language1Name !== languageNameValues.language2Name;
    const showL3Control =
        languageNameValues.language3Name &&
        languageNameValues.language3Name !== languageNameValues.language1Name &&
        languageNameValues.language3Name !== languageNameValues.language2Name;

    // This is a bit of a hack to figure out which of the cover title languages are turned on,
    // for use in possibly disabling one checkbox so they can't all be turned off.
    // It would look marginally better if we created a type for the settings object,
    // but that would introduce a lot of casting in communicating with Config-R.
    // If we had more than three, we'd want to go to keeping a count of how many are on.
    // But with just three the 'disable' boolean for each checkbox is manageable.
    // We can drop this if we decide to allow all three to be turned off (e.g. to allow the
    // user to embed the title in the image or make it a canvas element).
    const [showL1, showL2, showL3, numberShowing] = useMemo(() => {
        let appearance = props.settings?.["appearance"];
        if (props.settingsToReturnLater) {
            appearance = props.settingsToReturnLater["appearance"];
        }
        if (!appearance) {
            // This is a bit arbitrary. It should only apply during early renders.
            return [true, false, false, 1];
        }
        let count = 0;
        if (appearance[L1Field]) count++;
        if (showL2Control && appearance[L2Field]) count++;
        if (showL3Control && appearance[L3Field]) count++;

        return [
            appearance[L1Field],
            appearance[L2Field],
            appearance[L3Field],
            count,
        ];
    }, [
        L1Field,
        L2Field,
        L3Field,
        props.settings,
        props.settingsToReturnLater,
        showL2Control,
        showL3Control,
    ]);

    return (
        <div>
            <ConfigrBoolean
                label={props.labels.L1}
                disabled={props.disabled}
                // 'locked' will leave the label readable while making the checkbox disabled
                // 'locked' currently beats disabled, so it should not be locked if we want
                // the full disabled look.
                locked={
                    !props.disabled &&
                    ((numberShowing <= 1 && showL1) || props.L1MustBeTurnedOn)
                }
                {...props.getAdditionalProps(L1Field)}
            />
            {
                // Only makes sense to show a control for L2 if it is different from L1
                showL2Control && (
                    <ConfigrBoolean
                        label={props.labels.L2}
                        disabled={props.disabled}
                        // The second expression should never be false if L1 must be turned on;
                        // but we need it when that is false, and it's harmless when L1 must be on
                        // because showL1 will be true.
                        locked={!props.disabled && numberShowing <= 1 && showL2}
                        {...props.getAdditionalProps(L2Field)}
                    />
                )
            }

            {
                // Only show this one if it exists and is different from the others
                showL3Control && (
                    <ConfigrBoolean
                        label={props.labels.L3}
                        disabled={props.disabled}
                        locked={!props.disabled && numberShowing <= 1 && showL3}
                        {...props.getAdditionalProps(L3Field)}
                    />
                )
            }
        </div>
    );
};
