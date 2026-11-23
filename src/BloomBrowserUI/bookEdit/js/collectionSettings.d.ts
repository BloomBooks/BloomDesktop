// Type definitions for RuntimeInformationInjector injected GetSettings()
// If you want to use GetSettings() in your .ts file, reference this file.

declare function GetSettings(): ICollectionSettings;

interface ICollectionSettings {
    languageForNewTextBoxes: string;
    // The collection's first language (the role CL1), whatever the toolbar shows.
    collectionLanguage1: string;
    // The collection's second and third languages (the roles N1 and N2), when it has them.
    metadataLanguage1?: string;
    metadataLanguage2?: string;
    defaultSourceLanguage: string;
    defaultSourceLanguage2: string;
    currentCollectionLanguage2: string;
    currentCollectionLanguage3: string;
    browserRoot: string;
    topics: string[];
}
