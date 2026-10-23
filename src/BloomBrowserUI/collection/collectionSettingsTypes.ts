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
    // For the whole collection, a non-localized key.
    pageNumberStyle: string;
}

export interface ICollectionSettingsFrontBackMatter {
    xmatter: string;
    showQrCode: boolean;
    qrcodeCaption: string;
    country: string;
    province: string;
    district: string;
}

export interface ICollectionSettingsSubscription {
    // As the user typed it; "" for none.
    code: string;
}

export interface ICollectionSettingsTeamCollection {
    // A Team Collection's administrators, separated by commas or spaces.
    administrators: string;
    // The folder to make this collection a Team Collection in as it is saved; "" for none.
    createInFolder: string;
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
    subscription: ICollectionSettingsSubscription;
    teamCollection: ICollectionSettingsTeamCollection;
    bloomLibrary: ICollectionSettingsBloomLibrary;
    advanced: ICollectionSettingsAdvanced;
    // Keyed by experimental feature token.
    experimental: Record<string, boolean>;
    readerTools: ICollectionSettingsReaderTools;
}

// The two reader tools settings that describe the first language's script. They are kept in the
// reader tools' own file, named for the first language, beside the stages and levels.
export interface ICollectionSettingsReaderTools {
    // The letters and letter combinations, separated by spaces.
    letters: string;
    // Unicode escapes (like \U0020) of other characters that end a sentence.
    sentencePunct: string;
    // True when the file is damaged; then the two values above are empty and cannot be edited.
    isUnreadable: boolean;
}

// The reply to GET collection/qrCodeBadge (QrCodeBadgeData in C#).
export interface IQrCodeBadgeData {
    // A data: url of the QR code image.
    qrCode: string;
    // The address the badge shows beneath its caption.
    displayUrl: string;
    // The caption books get when the caption box is left empty.
    defaultCaption: string;
}

// The reply to GET collection/settings.
export interface ICollectionSettingsResponse {
    values: ICollectionSettingsValues;
    // Dotted paths into `values`; a change to any of them means Bloom must restart.
    restartPaths: string[];
    // Whether the open collection is a Team Collection, even if disconnected.
    isTeamCollection: boolean;
    // Whether the collection holds a book downloaded from Bloom Library for editing.
    editingABlorgBook: boolean;
    // The font a language gets when the user adds it (a third language).
    defaultFontName: string;
    // Non-null when the user may not edit the settings (a Team Collection member who is not an
    // administrator). Then the other fields are null.
    notAllowedMessage: string | null;
}
