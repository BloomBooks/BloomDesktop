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