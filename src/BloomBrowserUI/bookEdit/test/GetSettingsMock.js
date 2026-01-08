// NB: Do not save this as Typescript!!! Currently we need it as a separate js file that
// Karma can explicitly include. If you rename this to .ts, then no js will be created
// (because we just emit a bundled js file of all dependencies), and Karma won't find it.
// Also, write it for ES3, as it isn't (as of this writing) going through babel.

//Sadly, we get some data into JS-land by c# literally pushing the methods into the dom.
//Until we get rid of that (they should instead be ajax calls to the server)
//this file has static versions of those methods, for use in unit tests. Karma should include it.

/**
 * Test substitute for RuntimeInformationInjector.cs AddUISettingsToDom()
 * @returns {Object}
 */
function GetSettings() {
    var v = {};
    v.defaultSourceLanguage = "en";
    v.currentCollectionLanguage2 = "tpi";
    v.currentCollectionLanguage3 = "fr";
    v.languageForNewTextBoxes = "en";
    v.browserRoot = "";
    v.topics = [
        "Agriculture",
        "Animal Stories",
        "Business",
        "Culture",
        "Community Living",
        "Dictionary",
        "Environment",
        "Fiction",
        "Health",
        "How To",
        "Math",
        "Non Fiction",
        "Spiritual",
        "Personal Development",
        "Primer",
        "Science",
        "Tradition",
    ];
    return v;
}

//noinspection JSUnusedGlobalSymbols
/**
 * Test substitute for RuntimeInformationInjector.cs AddUIDictionaryToDom()
 * @returns {Object}
 */
function GetInlineDictionary() {
    return {
        en: "English",
        vernacularLang: "en",
        "{V}": "English",
        "{N1}": "English",
        "{N2}": "",
        ar: "العربية/عربي‎",
        id: "Bahasa Indonesia",
        ha: "Hausa",
        hi: "हिन्दी",
        es: "español",
        fr: "français",
        pt: "português",
        swa: "Swahili",
        th: "ภาษาไทย",
        tpi: "Tok Pisin",
        "TemplateBooks.PageLabel.Front Cover": "Front Cover",
        "BookEditor.FontSizeTip":
            "Changes the text size for all boxes carrying the style '{0}' and language '{1}'.\\nCurrent size is {2}pt.",
        "EditTab.FrontMatter.BookTitlePrompt": "Book title in {lang}",
        "EditTab.FrontMatter.TopicPrompt": "Click to choose topic",
        "EditTab.FrontMatter.ISBNPrompt":
            "International Standard Book Number. Leave blank if you don't have one of these.",
        "EditTab.FrontMatter.TranslatedAcknowledgmentsPrompt":
            "Acknowledgments for translated version, in {lang}",
        "EditTab.FrontMatter.FundingAgenciesPrompt":
            "Use this to acknowledge any funding agencies.",
        "EditTab.BackMatter.InsideBackCoverTextPrompt":
            "If you need somewhere to put more information about the book, you can use this page, which is the inside of the back cover.",
        "EditTab.BackMatter.OutsideBackCoverTextPrompt":
            "If you need somewhere to put more information about the book, you can use this page, which is the outside of the back cover.",
    };
}
