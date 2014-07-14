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

/**
 * These functions are called by C#, so the WebStorm code inspection thinks they are unused.
 */
function ForCodeInspection_UnusedFunctions() {

    // don't actually do anything
    if (1 === 1) return;

    initializeSynphony('', false);
    setSampleFileContents('');
    setTextsList('');
    closeSetupDialog();

}
