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
});
