import { describe, expect, it } from "vitest";
import {
    applyDefaultAppBuilderIconChoice,
    getAppBuilderPackageNameValidationIssue,
    getAppBuilderSettingsValidationIssues,
    getAboutTextPath,
    getAppBuilderSettingsFromAppDef,
    getDefaultAppBuilderIconChoice,
    hasRequiredBuildSettings,
    kDefaultAppBuilderIconId,
    normalizeIconChoice,
    normalizeSettings,
    updateAppBuilderAppDef,
} from "./appBuilderAppDef";

describe("appBuilderAppDef", () => {
    it("normalizes settings from PascalCase API payloads", () => {
        const settings = normalizeSettings({
            AppName: "Sample App",
            ColorScheme: "Lime",
            PackageName: "org.sil.sample",
            IconPath: "C:\\icons\\sample.png",
            Copyright: "Copyright 2026",
            About: "About this app",
        });

        expect(settings).toEqual({
            appName: "Sample App",
            colorScheme: "Lime",
            packageName: "org.sil.sample",
            iconPath: "C:\\icons\\sample.png",
            copyright: "Copyright 2026",
            about: "About this app",
        });
    });

    it("prefers bloom-app-icon-21 as the default icon choice", () => {
        const choices = [
            normalizeIconChoice({
                Id: "ab-001-black",
                IconPath: "C:\\icons\\black.png",
                Label: "ab-001-black",
            }),
            normalizeIconChoice({
                Id: kDefaultAppBuilderIconId,
                IconPath: "C:\\icons\\ornament.png",
                Label: kDefaultAppBuilderIconId,
            }),
        ];

        expect(getDefaultAppBuilderIconChoice(choices)?.iconPath).toBe(
            "C:\\icons\\ornament.png",
        );
    });

    it("fills in the default icon path only when settings are empty", () => {
        const choices = [
            normalizeIconChoice({
                Id: kDefaultAppBuilderIconId,
                IconPath: "C:\\icons\\ornament.png",
                Label: kDefaultAppBuilderIconId,
            }),
        ];

        expect(
            applyDefaultAppBuilderIconChoice(
                {
                    appName: "Sample App",
                    colorScheme: "Lime",
                    packageName: "org.sil.sample",
                    iconPath: "",
                    copyright: "Copyright 2026",
                    about: "",
                },
                choices,
            ).iconPath,
        ).toBe("C:\\icons\\ornament.png");

        expect(
            applyDefaultAppBuilderIconChoice(
                {
                    appName: "Sample App",
                    colorScheme: "Lime",
                    packageName: "org.sil.sample",
                    iconPath: "C:\\icons\\custom.png",
                    copyright: "Copyright 2026",
                    about: "",
                },
                choices,
            ).iconPath,
        ).toBe("C:\\icons\\custom.png");
    });

    it("requires both app name and package name before build", () => {
        expect(
            hasRequiredBuildSettings({
                appName: "Sample App",
                colorScheme: "Lime",
                packageName: "org.sil.sample",
                iconPath: "",
                copyright: "Copyright 2026",
                about: "About this app",
            }),
        ).toBe(true);

        expect(
            hasRequiredBuildSettings({
                appName: "   ",
                colorScheme: "Lime",
                packageName: "org.sil.sample",
                iconPath: "",
                copyright: "Copyright 2026",
                about: "About this app",
            }),
        ).toBe(false);

        expect(
            hasRequiredBuildSettings({
                appName: "Sample App",
                colorScheme: "Lime",
                packageName: "   ",
                iconPath: "",
                copyright: "Copyright 2026",
                about: "About this app",
            }),
        ).toBe(false);

        expect(
            hasRequiredBuildSettings({
                appName: "Sample App",
                colorScheme: "Lime",
                packageName: "org.SIL.sample",
                iconPath: "",
                copyright: "Copyright 2026",
                about: "About this app",
            }),
        ).toBe(false);

        expect(
            hasRequiredBuildSettings({
                appName: "Sample App",
                colorScheme: "Lime",
                packageName: "org.sil.sample",
                iconPath: "",
                copyright: "   ",
                about: "About this app",
            }),
        ).toBe(false);

        expect(
            hasRequiredBuildSettings({
                appName: "Sample App",
                colorScheme: "Lime",
                packageName: "org.sil.sample",
                iconPath: "",
                copyright: "Copyright 2026",
                about: "   ",
            }),
        ).toBe(false);
    });

    it("reports required and validation issues for app settings", () => {
        expect(
            getAppBuilderSettingsValidationIssues({
                appName: " ",
                colorScheme: "Indigo",
                packageName: "org.SIL.sample",
                iconPath: "",
                copyright: " ",
                about: "",
            }),
        ).toEqual({
            appName: "required",
            packageName: "invalid",
            copyright: "required",
            about: "required",
        });

        expect(
            getAppBuilderSettingsValidationIssues({
                appName: "Sample App",
                colorScheme: "Indigo",
                packageName: "org.sil.sample",
                iconPath: "",
                copyright: "Copyright 2026",
                about: "About this app",
            }),
        ).toEqual({
            appName: undefined,
            packageName: undefined,
            copyright: undefined,
            about: undefined,
        });
    });

    it("validates package names for the settings dialog", () => {
        expect(getAppBuilderPackageNameValidationIssue("   ")).toBe("required");
        expect(getAppBuilderPackageNameValidationIssue("org.sil.sample")).toBe(
            undefined,
        );
        expect(getAppBuilderPackageNameValidationIssue("org.SIL.sample")).toBe(
            "invalid",
        );
        expect(
            getAppBuilderPackageNameValidationIssue("org.sil.sample app"),
        ).toBe("invalid");
        expect(getAppBuilderPackageNameValidationIssue("sample")).toBe(
            "invalid",
        );
    });

    it("reads and updates app definition settings in the browser", () => {
        const originalAppDef = `<?xml version="1.0" encoding="utf-8"?>
<app-definition type="RAB" program-version="13.4">
  <project-name>Stories</project-name>
  <app-name lang="default">Stories</app-name>
  <package>org.sil.stories</package>
  <color-scheme name="Indigo" />
  <books id="C01">
    <metadata>
      <meta name="copyright-text" content="Copyright 2026" />
    </metadata>
  </books>
</app-definition>`;

        expect(
            getAppBuilderSettingsFromAppDef(
                "C:\\Collection\\app configuration\\stories.appDef",
                originalAppDef,
            ),
        ).toEqual({
            appName: "Stories",
            colorScheme: "Indigo",
            packageName: "org.sil.stories",
            iconPath: "",
            copyright: "Copyright 2026",
            about: "",
        });

        const updatedAppDef = updateAppBuilderAppDef(originalAppDef, {
            appName: "Updated Stories",
            colorScheme: "Lime",
            packageName: "org.sil.updated.stories",
            iconPath: "C:\\icons\\updated.png",
            copyright: "Updated copyright",
            about: "About this app",
        });

        expect(updatedAppDef).toContain(
            '<app-name lang="default">Updated Stories</app-name>',
        );
        expect(updatedAppDef).toContain(
            "<package>org.sil.updated.stories</package>",
        );
        expect(updatedAppDef).toContain('<color-scheme name="Lime"');
        expect(updatedAppDef).toContain('content="Updated copyright"');
        expect(updatedAppDef).toContain('<about enabled="true"');
        expect(updatedAppDef).toContain("<filename>about.txt</filename>");
        expect(updatedAppDef).toContain("drawable-web\\ic_launcher.png");
        expect(updatedAppDef).toContain(
            "<image>ic_launcher_foreground.png</image>",
        );
    });

    it("stores about text under the app configuration root for nested app defs", () => {
        expect(
            getAboutTextPath(
                "C:\\Users\\hatto\\Documents\\Bloom\\RAB Books\\app configuration\\RAB Books\\RAB Books.appDef",
            ),
        ).toBe(
            "C:\\Users\\hatto\\Documents\\Bloom\\RAB Books\\app configuration\\project-assets\\about.txt",
        );
    });
});
