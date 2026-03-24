export const kLegacyThemeName = "legacy-5-6";

export function isLegacyThemeName(themeName: string | undefined): boolean {
    return themeName === kLegacyThemeName;
}

export function isLegacyThemeCssLoaded(doc: Document = document): boolean {
    return !!doc.querySelector(
        `link[href*='basePage-${kLegacyThemeName}.css']`,
    );
}
