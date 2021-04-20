/// <reference path="localizationManager.ts" />
///<reference path="../../typings/bundledFromTSC.d.ts"/>
import theOneLocalizationManager from "./localizationManager";

"use strict";

describe("localizationManager", () => {
    beforeEach(() => {});

    /* This doesn't work. The old version would never fail, because it didn't actually account for how to handle
        async calls. I've adjusted it to be apparently correct, but now we run into the problem that the method under
        test here actually fails in some non-understoodway.  See BL-3554.

            it("asyncGetTextInLang returns English if in a unit test environment", function (done) {
                    theOneLocalizationManager.asyncGetTextInLang('theKey','someEnglishWord', 'fr').done(result => {
                            expect(result).toBe('someEnglishWord');
                            done();
                    })
                    .fail(result => {
                            fail(result);
                            done();
                    });
            }); */

    it("processSimpleMarkdown works properly", () => {
        const result1 = theOneLocalizationManager.processSimpleMarkdown(
            "This is a test."
        );
        expect(result1).toBe("This is a test.");

        const result2 = theOneLocalizationManager.processSimpleMarkdown(
            "This is a **test**."
        );
        expect(result2).toBe("This is a <strong>test</strong>.");

        const result3 = theOneLocalizationManager.processSimpleMarkdown(
            "This is a *test*."
        );
        expect(result3).toBe("This is a <em>test</em>.");

        const result4 = theOneLocalizationManager.processSimpleMarkdown(
            "This is a [test](https://sil.org)."
        );
        expect(result4).toBe('This is a <a href="https://sil.org">test</a>.');

        const result5 = theOneLocalizationManager.processSimpleMarkdown(
            "*This* is a **more** complex [test](https://wherever.com)**!!**"
        );
        expect(result5).toBe(
            '<em>This</em> is a <strong>more</strong> complex <a href="https://wherever.com">test</a><strong>!!</strong>'
        );

        const result6 = theOneLocalizationManager.processSimpleMarkdown(
            "This is a [**] test (*)."
        );
        expect(result6).toBe("This is a [**] test (*).");
    });

    it("simpleFormat replaces %0 and %1 with l10nParams", () => {
        const result = theOneLocalizationManager.simpleFormat(
            "%1 likes %0, but %0 does not like %1",
            ["Jack", "Jill"]
        );
        expect(result).toBe("Jill likes Jack, but Jack does not like Jill");
    });
    it("simpleFormat replaces {0} and {1} with l10nParams", () => {
        const result = theOneLocalizationManager.simpleFormat(
            "{1} likes {0}, but {0} does not like {1}",
            ["Jack", "Jill"]
        );
        expect(result).toBe("Jill likes Jack, but Jack does not like Jill");
    });
    it("simpleFormat does not replace missing params", () => {
        const result = theOneLocalizationManager.simpleFormat(
            "{1} likes {0}, but {7} does not like %8",
            ["Jack", undefined]
        );
        expect(result).toBe("{1} likes Jack, but {7} does not like %8");
    });

    it("simpleFormat can insert empty string", () => {
        const result = theOneLocalizationManager.simpleFormat(
            "the translation of '{0}' is '{1}'",
            ["Jack", ""]
        );
        expect(result).toBe("the translation of 'Jack' is ''");
    });
});
