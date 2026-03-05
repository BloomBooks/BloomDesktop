import { css } from "@emotion/react";
import { ConfigrBoolean } from "@sillsdev/config-r";
import * as React from "react";
import { useApiObject } from "../../utils/bloomApi";
import { useL10n } from "../../react_components/l10nHooks";
import { useMemo } from "react";

interface ILanguageNameValues {
    language1Name: string;
    language2Name: string;
    language3Name?: string;
}

// This is used in BookSettingsDialog to provide a group of Config-R booleans that control
// which languages to show for a specified field.
// It is currently used for the cover title languages.
// It controls values for three Config-R booleans, called <field>-L1-show, <field>-L2-show, and <field>-L3-show.
export const FieldVisibilityGroup: React.FunctionComponent<{
    field: string; // the field we're configuring, e.g. "cover-title", without the "-LN-show" suffix
    labelFrame: string;
    labelFrameL10nKey: string;
    settings: object | undefined;
    settingsToReturnLater: string | object | undefined;
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
    // Enhance: we may decide to let the dialog retrieve these and pass them in, to reduce API calls,
    // if we actually start having more than one field configured with this component.
    const languageNameValues: ILanguageNameValues =
        useApiObject<ILanguageNameValues>("settings/languageNames", {
            language1Name: "",
            language2Name: "",
        });
    const showWrittenLanguage1TitleLabel = useL10n(
        props.labelFrame,
        props.labelFrameL10nKey,
        "",
        languageNameValues.language1Name,
    );
    const showWrittenLanguage2TitleLabel = useL10n(
        props.labelFrame,
        props.labelFrameL10nKey,
        "",
        languageNameValues.language2Name,
    );
    const showWrittenLanguage3TitleLabel = useL10n(
        props.labelFrame,
        props.labelFrameL10nKey,
        undefined,
        languageNameValues.language3Name,
    );

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
            // although we originally declared it a string, Config-R may return a JSON string or an object
            if (typeof props.settingsToReturnLater === "string") {
                const parsedSettings = JSON.parse(props.settingsToReturnLater);
                appearance = parsedSettings["appearance"];
            } else {
                appearance = props.settingsToReturnLater["appearance"];
            }
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
                label={showWrittenLanguage1TitleLabel}
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
                        label={showWrittenLanguage2TitleLabel}
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
                        label={showWrittenLanguage3TitleLabel}
                        disabled={props.disabled}
                        locked={!props.disabled && numberShowing <= 1 && showL3}
                        {...props.getAdditionalProps(L3Field)}
                    />
                )
            }
        </div>
    );
};
