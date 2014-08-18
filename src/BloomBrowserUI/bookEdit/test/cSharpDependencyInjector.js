//noinspection JSUnusedGlobalSymbols
/**
 * Test substitute for RuntimeInformationInjector.cs AddUISettingsToDom()
 * @returns {Object}
 */
function GetSettings() {
    var v = {};
    v.defaultSourceLanguage = 'en';
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
function ForCodeInspection_UnusedFunctions() {

    // don't actually do anything
    if (1 === 1) return;

    setSampleFileContents('');
    setTextsList('');
    closeSetupDialog();
    SetCopyrightAndLicense(null);
    loadAccordionPanel('', '');
    restoreAccordionSettings('');

    var x = model.fontName;
}

//noinspection JSUnusedGlobalSymbols
/**
 * Test substitute for RuntimeInformationInjector.cs AddUIDictionaryToDom()
 * @returns {Object}
 */
function GetDictionary() {
    return {"en":"English","vernacularLang":"en","{V}":"English","{N1}":"English","{N2}":"","ha":"Hausa","hi":"Hindi","es":"Spanish","fr":"French","pt":"Portuguese","swa":"Swahili","th":"Thai","tpi":"Tok Pisin","EditTab.ThumbnailCaptions.Front Cover":"Front Cover","*You may use this space for author/illustrator, or anything else.":"*You may use this space for author/illustrator, or anything else.","Click to choose topic":"Click to choose topic","BookEditor.FontSizeTip":"Changes the text size for all boxes carrying the style '{0}' and language '{1}'.\\nCurrent size is {2}pt.","FrontMatter.Factory.Book title in {lang}":"Book title in {lang}","FrontMatter.Factory.Click to choose topic":"Click to choose topic","FrontMatter.Factory.International Standard Book Number. Leave blank if you don't have one of these.":"International Standard Book Number. Leave blank if you don't have one of these.","FrontMatter.Factory.Acknowledgments for translated version, in {lang}":"Acknowledgments for translated version, in {lang}","FrontMatter.Factory.Use this to acknowledge any funding agencies.":"Use this to acknowledge any funding agencies.","BackMatter.Factory.If you need somewhere to put more information about the book, you can use this page, which is the inside of the back cover.":"If you need somewhere to put more information about the book, you can use this page, which is the inside of the back cover.","BackMatter.Factory.If you need somewhere to put more information about the book, you can use this page, which is the outside of the back cover.":"If you need somewhere to put more information about the book, you can use this page, which is the outside of the back cover."};
}