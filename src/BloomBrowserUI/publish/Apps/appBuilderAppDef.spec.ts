import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../../utils/bloomApi", () => ({
    getWithPromise: vi.fn(),
    postJsonAsync: vi.fn(),
    useApiObject: vi.fn(),
}));

import { getWithPromise, postJsonAsync } from "../../utils/bloomApi";
import {
    applyDefaultAppBuilderIconChoice,
    fetchAppBuilderSettings,
    fetchDefaultAppBuilderSettings,
    getAppBuilderPackageNameValidationIssue,
    getAppBuilderSettingsValidationIssues,
    getAboutTextPath,
    getAppBuilderSettingsFromAppDef,
    getDefaultAppBuilderIconChoice,
    hasRequiredBuildSettings,
    initializeAppBuilderSettings,
    kDefaultAppBuilderIconId,
    normalizeIconChoice,
    updateAppBuilderAppDef,
} from "./appBuilderAppDef";

const getWithPromiseMock = vi.mocked(getWithPromise);
const postJsonAsyncMock = vi.mocked(postJsonAsync);

describe("appBuilderAppDef", () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it("prefers bloom-app-icon-52 as the default icon choice", () => {
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
                "C:\\Collection\\Bloom App Data\\stories.appDef",
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

    it("keeps app name blank when the raw appDef app-name is blank", () => {
        const appDefWithBlankAppName = `<?xml version="1.0" encoding="utf-8"?>
<app-definition type="RAB" program-version="13.4">
  <project-name>Stories</project-name>
  <app-name lang="default"/>
  <package>org.sil.stories</package>
</app-definition>`;

        expect(
            getAppBuilderSettingsFromAppDef(
                "C:\\Collection\\Bloom App Data\\stories.appDef",
                appDefWithBlankAppName,
            ).appName,
        ).toBe("");
    });

    it("stores about text under the Bloom App Data root for nested app defs", () => {
        expect(
            getAboutTextPath(
                "C:\\Users\\hatto\\Documents\\Bloom\\RAB Books\\Bloom App Data\\RAB Books\\RAB Books.appDef",
            ),
        ).toBe(
            "C:\\Users\\hatto\\Documents\\Bloom\\RAB Books\\Bloom App Data\\project-assets\\about.txt",
        );
    });

    it("loads app settings from the raw appDef and about file", async () => {
        postJsonAsyncMock
            .mockResolvedValueOnce({
                data: `<?xml version="1.0" encoding="utf-8"?>
<app-definition type="RAB" program-version="13.4">
  <project-name>Stories</project-name>
  <app-name lang="default"/>
  <package>org.sil.stories</package>
  <color-scheme name="Indigo" />
  <books id="C01">
    <metadata>
      <meta name="copyright-text" content="Copyright 2026" />
    </metadata>
  </books>
</app-definition>`,
            } as never)
            .mockResolvedValueOnce({
                data: "About this app",
            } as never);

        const settings = await fetchAppBuilderSettings(
            "C:\\Users\\hatto\\Documents\\Bloom\\RAB Books\\Bloom App Data\\RAB Books\\RAB Books.appDef",
        );

        expect(postJsonAsyncMock).toHaveBeenNthCalledWith(
            1,
            "fileIO/readFile",
            {
                path: "C:\\Users\\hatto\\Documents\\Bloom\\RAB Books\\Bloom App Data\\RAB Books\\RAB Books.appDef",
            },
        );
        expect(postJsonAsyncMock).toHaveBeenNthCalledWith(
            2,
            "fileIO/readFile",
            {
                path: "C:\\Users\\hatto\\Documents\\Bloom\\RAB Books\\Bloom App Data\\project-assets\\about.txt",
            },
        );
        expect(settings).toEqual({
            appName: "",
            colorScheme: "Indigo",
            packageName: "org.sil.stories",
            iconPath: "",
            copyright: "Copyright 2026",
            about: "About this app",
        });
    });

    it("loads collection-derived defaults from the backend without reading the appDef", async () => {
        getWithPromiseMock.mockResolvedValue({
            data: {
                AppName: "Stories",
                ColorScheme: "Indigo",
                PackageName: "org.sil.stories",
                IconPath:
                    "C:\\Bloom\\DistFiles\\appbuilder-icons\\bloom-app-icon-52.png",
                Copyright: "Copyright 2026",
                About: "Created with Bloom.",
            },
        } as never);

        const settings = await fetchDefaultAppBuilderSettings();

        expect(getWithPromiseMock).toHaveBeenCalledWith(
            "publish/rab/default-settings",
        );
        expect(settings).toEqual({
            appName: "Stories",
            colorScheme: "Indigo",
            packageName: "org.sil.stories",
            iconPath:
                "C:\\Bloom\\DistFiles\\appbuilder-icons\\bloom-app-icon-52.png",
            copyright: "Copyright 2026",
            about: "Created with Bloom.",
        });
    });

    it("initializes missing raw app settings by saving through the frontend", async () => {
        const appDefPath =
            "C:\\Users\\hatto\\Documents\\Bloom\\RAB Books\\Bloom App Data\\RAB Books\\RAB Books.appDef";
        const rawAppDef = `<?xml version="1.0" encoding="utf-8"?>
<app-definition type="RAB" program-version="13.4">
  <project-name>Stories</project-name>
  <app-name lang="default"/>
  <package/>
  <color-scheme name="Indigo" />
  <books id="C01">
    <metadata />
  </books>
</app-definition>`;

        getWithPromiseMock.mockResolvedValue({
            data: {
                AppName: "Stories",
                ColorScheme: "Indigo",
                PackageName: "org.sil.stories",
                Copyright: "Copyright 2026",
                About: "Created with Bloom.",
            },
        } as never);
        postJsonAsyncMock
            .mockResolvedValueOnce({ data: rawAppDef } as never)
            .mockResolvedValueOnce({ data: "" } as never)
            .mockResolvedValueOnce({ data: rawAppDef } as never)
            .mockResolvedValueOnce({} as never)
            .mockResolvedValueOnce({} as never);

        const settings = await initializeAppBuilderSettings(appDefPath);

        expect(settings).toEqual({
            appName: "Stories",
            colorScheme: "Indigo",
            packageName: "org.sil.stories",
            iconPath: "",
            copyright: "Copyright 2026",
            about: "Created with Bloom.",
        });

        const aboutWriteCall = postJsonAsyncMock.mock.calls.find(
            (call) =>
                call[0] === "fileIO/writeFile" &&
                (call[1] as { path: string }).path ===
                    "C:\\Users\\hatto\\Documents\\Bloom\\RAB Books\\Bloom App Data\\project-assets\\about.txt",
        );
        expect(aboutWriteCall?.[1]).toEqual({
            path: "C:\\Users\\hatto\\Documents\\Bloom\\RAB Books\\Bloom App Data\\project-assets\\about.txt",
            content: "Created with Bloom.",
        });

        const appDefWriteCall = postJsonAsyncMock.mock.calls.find(
            (call) =>
                call[0] === "fileIO/writeFile" &&
                (call[1] as { path: string }).path === appDefPath,
        );
        const writtenAppDef = (appDefWriteCall?.[1] as { content: string })
            .content;
        expect(writtenAppDef).toContain(
            '<app-name lang="default">Stories</app-name>',
        );
        expect(writtenAppDef).toContain("<package>org.sil.stories</package>");
        expect(writtenAppDef).toContain('content="Copyright 2026"');
    });
});
