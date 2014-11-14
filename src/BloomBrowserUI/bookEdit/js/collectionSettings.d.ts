// Type definitions for RuntimeInformationInjector injected GetSettings()
// If you want to use GetSettings() in your .ts file, reference this file.

declare function GetSettings(): settingsObject;

interface settingsObject {
  isSourceCollection: boolean;
  languageForNewTextBoxes: string;
  defaultSourceLanguage: string;
  bloomBrowserUIFolder: string;
  topics: string[];
}