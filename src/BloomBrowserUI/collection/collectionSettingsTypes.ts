// The shape of what the `collection/settings` endpoints send and receive.
// The C# DTOs in CollectionSettingsTypes.cs must match these interfaces.

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
    // Null when the collection has no third language; posting null removes it.
    language3: ICollectionSettingsLanguage | null;
    // Null when the collection has no sign language; posting null removes it.
    signLanguage: ICollectionSettingsSignLanguage | null;
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

export interface ICollectionSettingsBloomLibrary {
    // The url key of the bookshelf uploaded books go into, or "" for none.
    defaultBookshelf: string;
}

export interface ICollectionSettingsAdvanced {
    autoUpdate: boolean;
    collectionName: string;
}

// The settings the user can edit. This whole object is what we POST back on OK.
export interface ICollectionSettingsValues {
    languages: ICollectionSettingsLanguages;
    frontBackMatter: ICollectionSettingsFrontBackMatter;
    bloomLibrary: ICollectionSettingsBloomLibrary;
    advanced: ICollectionSettingsAdvanced;
    // Keyed by experimental feature token.
    experimental: Record<string, boolean>;
}

// The reply to GET collection/settings.
export interface ICollectionSettingsResponse {
    values: ICollectionSettingsValues;
    // Dotted paths into `values`; a change to any of them means Bloom must restart.
    restartPaths: string[];
    // Whether the open collection is a Team Collection, even if disconnected.
    isTeamCollection: boolean;
    // Non-null when the user may not edit the settings (a Team Collection member who is not an
    // administrator). Then the other fields are null.
    notAllowedMessage: string | null;
}
