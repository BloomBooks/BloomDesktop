// Type definitions for RuntimeInformationInjector injected GetSettings()
// If you want to use GetSettings() in your .ts file, reference this file.

declare function GetSettings(): ICollectionSettings;

interface ICollectionSettings {
    languageForNewTextBoxes: string;
    defaultSourceLanguage: string;
    defaultSourceLanguage2: string;
    currentCollectionLanguage2: string;
    currentCollectionLanguage3: string;
    browserRoot: string;
    topics: string[];
}
