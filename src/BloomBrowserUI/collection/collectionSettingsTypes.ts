// The shape of what the `collection/settings` endpoints send and receive.
// The C# DTOs in CollectionSettingsApi.cs must match these interfaces.

// Everything the dialog can show about one of the collection's text languages.
export interface ICollectionSettingsLanguage {
    tag: string;
    name: string;
    isCustomName: boolean;
    fontName: string;
    isRightToLeft: boolean;
    lineHeight: number;
    breaksLinesOnlyAtSpaces: boolean;
    baseUIFontSizeInPoints: number;
}

// The sign language has no font or script properties, so it carries only the identity fields.
export interface ICollectionSettingsSignLanguage {
    tag: string;
    name: string;
    isCustomName: boolean;
}

export interface ICollectionSettingsLanguages {
    language1: ICollectionSettingsLanguage;
    language2: ICollectionSettingsLanguage;
    // C# sends null rather than omitting the property when the collection has no third language.
    // Posting null back leaves the third language as it is; to remove it, post it with an empty
    // tag (the same rule as for the sign language).
    language3: ICollectionSettingsLanguage | null;
    signLanguage: ICollectionSettingsSignLanguage;
}

export interface ICollectionSettingsFrontBackMatter {
    xmatter: string;
    pageNumberStyle: string;
    showQrCode: boolean;
    qrcodeCaption: string;
    country: string;
    province: string;
    district: string;
}

export interface ICollectionSettingsAdvanced {
    autoUpdate: boolean;
    collectionName: string;
}

// The settings the user can edit. This whole object is what we POST back on OK.
export interface ICollectionSettingsValues {
    languages: ICollectionSettingsLanguages;
    frontBackMatter: ICollectionSettingsFrontBackMatter;
    advanced: ICollectionSettingsAdvanced;
    // Keyed by experimental feature token.
    experimental: Record<string, boolean>;
}

export interface IXmatterOffering {
    displayName: string;
    internalName: string;
    description: string;
}

export interface INumberingStyleOffering {
    localizedStyle: string;
    styleKey: string;
}

// Read-only information the pages need in order to render, but that the user cannot edit here.
export interface ICollectionSettingsContext {
    isTeamCollection: boolean;
    editingBlorgBook: boolean;
    showAutoUpdate: boolean;
    teamCollectionsAllowed: boolean;
    xmatterOfferings: IXmatterOffering[];
    // Non-null when the branding dictates the front/back matter pack, which locks that control.
    brandingForcedXmatter: string | null;
    numberingStyles: INumberingStyleOffering[];
}

// The reply to GET collection/settings. The GET also opens the editing session on the C# side.
export interface ICollectionSettingsResponse {
    values: ICollectionSettingsValues;
    context: ICollectionSettingsContext;
    // Dotted paths into `values`; a change to any of them means Bloom must restart.
    restartPaths: string[];
    // Non-null when the user may not edit the settings (a Team Collection member who is not an
    // administrator). Then no session was opened and the other fields are null.
    notAllowedMessage: string | null;
}

// The reply to POST collection/settings. A non-null errorMessage means nothing was saved.
export interface ICollectionSettingsSaveResult {
    restartRequired: boolean;
    errorMessage: string | null;
}
