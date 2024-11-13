import path from "path";
import process from "node:process";
import core from "./rspack.core.js";
import * as glob from "fast-glob";

const outputDir = "../../output/browser";
const pathToOriginalJavascriptFilesInLib = path.resolve("lib");
const pathToBookEditJS = path.resolve("bookEdit/js");
const pathToOriginalJavascriptFilesInModified_Libraries = path.resolve(
    "modified_libraries"
);
export default {
    tools: {
        rspack: (config, { mergeConfig }) => {
            return mergeConfig(config, {
                context: process.cwd(),
                devtool: "eval",
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
                    accessibilityCheckBundle: globule.find([
                        "./publish/accessibilityCheck/**/*.tsx"
                    ]),
                    enterpriseSettingsBundle:
                        "./collection/enterpriseSettings.tsx",

                    performanceLogBundle:
                        "./performance/PerformanceLogPage.tsx",
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
                    bookMakingSettingsBundle:
                        "./collection/bookMakingSettingsControl.tsx",
                    progressDialogBundle:
                        "./react_components/Progress/ProgressDialog.tsx",
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
                    duplicateManyDlgBundle:
                        "./bookEdit/duplicateManyDialog.tsx",
                    copyrightAndLicenseBundle:
                        "./bookEdit/copyrightAndLicense/CopyrightAndLicenseDialog.tsx",
                    collectionsTabPaneBundle:
                        "./collectionsTab/CollectionsTabPane.tsx",
                    publishTabPaneBundle:
                        "./publish/PublishTab/PublishTabPane.tsx"
                },

                output: {
                    path: path.join(process.cwd(), outputDir),
                    filename: "[name].js",
                    library: {
                        type: "var",
                        name: "[name]"
                    }
                },

                optimization: {
                    minimize: false,
                    moduleIds: "named",
                    splitChunks: {
                        cacheGroups: {
                            commons: {
                                name: "commonBundle",
                                chunks: "initial",
                                minChunks: 9,
                                minSize: 30000,
                                reuseExistingChunk: true
                            }
                        }
                    }
                },

                module: {
                    rules: [
                        {
                            test: /\.(js|jsx)$/,
                            exclude: [
                                /node_modules/,
                                /ckeditor/,
                                /jquery-ui/,
                                /-min/,
                                /qtip/,
                                /xregexp-all-min.js/
                            ],
                            use: [
                                {
                                    loader: "babel-loader",
                                    options: {
                                        presets: [
                                            [
                                                "@babel/preset-env",
                                                { targets: { edge: "112" } }
                                            ],
                                            "@babel/preset-react"
                                        ]
                                    }
                                }
                            ]
                        },
                        {
                            test: /\.woff(\?v=\d+\.\d+\.\d+)?$/,
                            type: "asset",
                            parser: {
                                dataUrlCondition: {
                                    maxSize: 10000
                                }
                            }
                        }
                    ]
                }
            });
        }
    }
};
