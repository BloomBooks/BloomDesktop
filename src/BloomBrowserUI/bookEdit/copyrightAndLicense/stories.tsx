import * as React from "react";
import { ThemeProvider } from "@mui/styles";
import { addDecorator, ComponentStory } from "@storybook/react";
import { StorybookContext } from "../../.storybook/StoryBookContext";
import { lightTheme } from "../../bloomMaterialUITheme";
import {
    CopyrightAndLicenseDialog,
    ICopyrightAndLicenseData
} from "./CopyrightAndLicenseDialog";
import { normalDialogEnvironmentForStorybook } from "../../react_components/BloomDialog/BloomDialogPlumbing";
import { CopyrightPanel } from "./CopyrightPanel";
import { LicensePanel, LicenseType } from "./LicensePanel";
import { LicenseBadge } from "./LicenseBadge";

addDecorator(storyFn => (
    <ThemeProvider theme={lightTheme}>
        <StorybookContext.Provider value={true}>
            {storyFn()}
        </StorybookContext.Provider>
    </ThemeProvider>
));

export default {
    title: "Copyright and License Dialog"
};

const CopyrightAndLicenseDialogTemplate: ComponentStory<typeof CopyrightAndLicenseDialog> = args => (
    <CopyrightAndLicenseDialog
        {...args}
        dialogEnvironment={normalDialogEnvironmentForStorybook}
    />
);
export const _CopyrightAndLicenseDialog = CopyrightAndLicenseDialogTemplate.bind(
    {}
);
const sampleCreativeCommonsInfo = {
    allowCommercial: "yes",
    allowDerivatives: "yes",
    intergovernmentalVersion: false
};
const sampleCopyrightAndLicenseData: ICopyrightAndLicenseData = {
    copyrightInfo: {
        imageCreator: "my image guy",
        copyrightYear: "",
        copyrightHolder: "my holder"
    },
    licenseInfo: {
        licenseType: LicenseType.PublicDomain,
        rightsStatement: "",
        creativeCommonsInfo: sampleCreativeCommonsInfo
    },
    derivativeInfo: {
        useOriginalCopyright: false,
        isBookDerivative: true,
        originalLicense: {
            licenseType: LicenseType.Custom,
            rightsStatement: "",
            creativeCommonsInfo: sampleCreativeCommonsInfo
        }
    }
};
_CopyrightAndLicenseDialog.args = {
    isForBook: true,
    data: sampleCopyrightAndLicenseData
};

const PanelFrame: React.FunctionComponent<{}> = props => (
    <div
        style={{
            height: 400,
            width: 400
        }}
    >
        {props.children}
    </div>
);

const CopyrightPanelTemplate: ComponentStory<typeof CopyrightPanel> = args => {
    const [valid, setValid] = React.useState(false);
    return (
        <PanelFrame>
            <CopyrightPanel
                {...args}
                copyrightInfo={{
                    copyrightYear: "2000",
                    copyrightHolder: "Bob"
                }}
                onChange={(
                    copyrightInfo,
                    useOriginalCopyrightAndLicense,
                    isValid
                ) => setValid(isValid)}
            />
            Valid: {valid.toString()}
        </PanelFrame>
    );
};
export const _CopyrightPanel = CopyrightPanelTemplate.bind({});
_CopyrightPanel.args = {
    isForBook: true
};

export const _LicensePanel = () => {
    const [valid, setValid] = React.useState(false);
    return (
        <PanelFrame>
            <LicensePanel
                isForBook={true}
                licenseInfo={{
                    licenseType: "creativeCommons",
                    creativeCommonsInfo: sampleCreativeCommonsInfo,
                    rightsStatement: ""
                }}
                onChange={(licenseInfo, isValid) => setValid(isValid)}
            />
            Valid: {valid.toString()}
        </PanelFrame>
    );
};

const LicenseBadgeTemplate: ComponentStory<typeof LicenseBadge> = args => {
    return <LicenseBadge {...args}></LicenseBadge>;
};
export const _LicenseBadge = LicenseBadgeTemplate.bind({});
_LicenseBadge.args = {
    licenseInfo: {
        licenseType: LicenseType.Custom,
        creativeCommonsInfo: sampleCreativeCommonsInfo
    },
    disabled: false
};
