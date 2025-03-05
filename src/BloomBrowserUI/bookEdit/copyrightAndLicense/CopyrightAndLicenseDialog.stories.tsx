import * as React from "react";
import type { Meta, StoryObj } from "@storybook/react";
import {
    CopyrightAndLicenseDialog,
    ICopyrightAndLicenseData
} from "./CopyrightAndLicenseDialog";
import { normalDialogEnvironmentForStorybook } from "../../react_components/BloomDialog/BloomDialogPlumbing";
import { CopyrightPanel } from "./CopyrightPanel";
import { LicensePanel, LicenseType } from "./LicensePanel";
import { LicenseBadge } from "./LicenseBadge";

const meta: Meta = {
    title: "Copyright and License Dialog"
};

export default meta;

// Define types for each component story
type CopyrightAndLicenseDialogStory = StoryObj<typeof CopyrightAndLicenseDialog>;
type CopyrightPanelStory = StoryObj<typeof CopyrightPanel>;
type LicenseBadgeStory = StoryObj<typeof LicenseBadge>;

// Sample data
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

// CopyrightAndLicenseDialog Story
export const CopyrightAndLicenseDialogStory: CopyrightAndLicenseDialogStory = {
    name: "Copyright And License Dialog",
    render: (args) => (
        <CopyrightAndLicenseDialog
            {...args}
            dialogEnvironment={normalDialogEnvironmentForStorybook}
        />
    ),
    args: {
        isForBook: true,
        data: sampleCopyrightAndLicenseData
    }
};

// PanelFrame component for framing panels
const PanelFrame: React.FunctionComponent<{ children: React.ReactNode }> = ({ children }) => (
    <div
        style={{
            height: 400,
            width: 400
        }}
    >
        {children}
    </div>
);

// CopyrightPanel Story
export const CopyrightPanelStory: CopyrightPanelStory = {
    name: "Copyright Panel",
    render: (args) => {
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
    },
    args: {
        isForBook: true
    }
};

// LicensePanel Story
export const LicensePanelStory: StoryObj = {
    name: "License Panel",
    render: () => {
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
    }
};

// LicenseBadge Story
export const LicenseBadgeStory: LicenseBadgeStory = {
    name: "License Badge",
    render: (args) => <LicenseBadge {...args} />,
    args: {
        licenseInfo: {
            licenseType: LicenseType.Custom,
            creativeCommonsInfo: sampleCreativeCommonsInfo,
            rightsStatement: "This is a rights statement",
        },

        disabled: false
    }
};
