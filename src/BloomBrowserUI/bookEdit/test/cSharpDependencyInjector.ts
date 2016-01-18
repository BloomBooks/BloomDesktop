/// <reference path="../js/calledByCSharp.ts" />
import {CalledByCSharp} from "../js/calledByCSharp";

import {ReaderToolsModel} from "../toolbox/decodableReader/readerToolsModel";
interface InjectorWindow extends Window {


  restoreAccordionSettings(val: string): void;
}

/**
 * Test substitute for RuntimeInformationInjector.cs AddUISettingsToDom()
 * @returns {Object}
 */
function GetSettings(): any {
  var v: any = {};
  v.defaultSourceLanguage = 'en';
  v.currentCollectionLanguage2 = 'tpi';
  v.currentCollectionLanguage3 = 'fr';
  v.languageForNewTextBoxes = 'en';
  v.isSourceCollection = 'false';
  v.bloomBrowserUIFolder = '';
  v.topics = ['Agriculture', 'Animal Stories', 'Business', 'Culture', 'Community Living', 'Dictionary', 'Environment', 'Fiction', 'Health', 'How To', 'Math', 'Non Fiction', 'Spiritual', 'Personal Development', 'Primer', 'Science', 'Tradition'];
  return v;
}

//noinspection JSUnusedGlobalSymbols
/**
 * These functions are called by C#, so the WebStorm code inspection thinks they are unused.
 */
function ForCodeInspection_UnusedFunctions(): void {

  // don't actually do anything
  if (1 === 1) return;

  (<InjectorWindow>window).restoreAccordionSettings('');

  var x = ReaderToolsModel.model.fontName;
  var calledByCSharpObj = new CalledByCSharp();
  calledByCSharpObj.removeSynphonyMarkup();
}

//noinspection JSUnusedGlobalSymbols
/**
 * Test substitute for RuntimeInformationInjector.cs AddUIDictionaryToDom()
 * @returns {Object}
 */
function GetInlineDictionary() {
    return { "en": "English", "vernacularLang": "en", "{V}": "English", "{N1}": "English", "{N2}": "", "ar": "العربية/عربي‎", "id": "Bahasa Indonesia", "ha": "Hausa", "hi": "हिन्दी", "es": "español", "fr": "français", "pt": "português", "swa": "Swahili", "th": "ภาษาไทย", "tpi": "Tok Pisin", "TemplateBooks.PageLabel.Front Cover": "Front Cover", "BookEditor.FontSizeTip": "Changes the text size for all boxes carrying the style '{0}' and language '{1}'.\\nCurrent size is {2}pt.", "EditTab.FrontMatter.BookTitlePrompt": "Book title in {lang}", "EditTab.FrontMatter.TopicPrompt": "Click to choose topic", "EditTab.FrontMatter.ISBNPrompt": "International Standard Book Number. Leave blank if you don't have one of these.", "EditTab.FrontMatter.TranslatedAcknowledgmentsPrompt": "Acknowledgments for translated version, in {lang}", "EditTab.FrontMatter.FundingAgenciesPrompt": "Use this to acknowledge any funding agencies.", "EditTab.BackMatter.InsideBackCoverTextPrompt": "If you need somewhere to put more information about the book, you can use this page, which is the inside of the back cover.", "EditTab.BackMatter.OutsideBackCoverTextPrompt": "If you need somewhere to put more information about the book, you can use this page, which is the outside of the back cover." };
}