//import common from "./rspack.common.js";
import * as glob from "fast-glob";
export default {
    //  ...common,
    mode: "development",
    entry: {
        editTabBundle: "./bookEdit/editViewFrame.ts",
        readerSetupBundle:
            "./bookEdit/toolbox/readers/readerSetup/readerSetup.ts",
        editablePageBundle: "./bookEdit/editablePage.ts",
        bookPreviewBundle:
            "./collectionsTab/collectionsTabBookPane/bookPreview.ts",
        toolboxBundle: "./bookEdit/toolbox/toolboxBootstrap.ts",
        spreadsheetBundle: "./spreadsheet/spreadsheetBundleRoot.ts",
        pageThumbnailListBundle:
            "./bookEdit/pageThumbnailList/pageThumbnailList.tsx",
        pageControlsBundle:
            "./bookEdit/pageThumbnailList/pageControls/pageControls.tsx",
        accessibilityCheckBundle: glob.glob.sync([
            "./publish/accessibilityCheck/**/*.tsx"
        ]),
        enterpriseSettingsBundle: "./collection/enterpriseSettings.tsx",

        performanceLogBundle: "./performance/PerformanceLogPage.tsx",
        appBundle: "./app/App.tsx",
        testBundle: glob.glob.sync([
            "./bookEdit/**/*Spec.ts",
            "./bookEdit/**/*Spec.js",
            "./lib/**/*Spec.ts",
            "./lib/**/*Spec.js",
            "./publish/**/*Spec.ts",
            "./publish/**/*Spec.js",
            "./react_components/**/*Spec.ts",
            "./react_components/**/*.spec.ts",
            "./utils/**/*Spec.ts"
        ]),

        problemReportBundle: "./problemDialog/ProblemDialog.tsx",
        messageBoxBundle: "./utils/BloomMessageBox.tsx",
        bookMakingSettingsBundle: "./collection/bookMakingSettingsControl.tsx",
        progressDialogBundle: "./react_components/Progress/ProgressDialog.tsx",
        requiresBloomEnterpriseBundle:
            "./react_components/requiresBloomEnterprise.tsx",
        createTeamCollectionDialogBundle:
            "./teamCollection/CreateTeamCollection.tsx",
        teamCollectionSettingsBundle:
            "./teamCollection/TeamCollectionSettingsPanel.tsx",
        joinTeamCollectionDialogBundle:
            "./teamCollection/JoinTeamCollectionDialog.tsx",
        autoUpdateSoftwareDlgBundle:
            "./react_components/AutoUpdateSoftwareDialog.tsx",
        duplicateManyDlgBundle: "./bookEdit/duplicateManyDialog.tsx",
        copyrightAndLicenseBundle:
            "./bookEdit/copyrightAndLicense/CopyrightAndLicenseDialog.tsx",
        collectionsTabPaneBundle: "./collectionsTab/CollectionsTabPane.tsx",
        publishTabPaneBundle: "./publish/PublishTab/PublishTabPane.tsx"
    },
    devtool: "source-map",
    devServer: {
        static: "./dist"
    },
    module: {
        rules: [
            {
                test: /\.less$/,
                use: [
                    "style-loader",
                    "css-loader",
                    {
                        loader: "less-loader",
                        options: {
                            lessOptions: {
                                javascriptEnabled: true
                            }
                        }
                    }
                ],
                type: "javascript/auto"
            }
        ]
    }
};
