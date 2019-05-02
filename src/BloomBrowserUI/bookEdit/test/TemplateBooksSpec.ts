// Slightly awkward place, this file is for testing stuff related to template books.
import { sendAnalytics } from "../../templates/template books/Special/simpleComprehensionQuiz";

function setupPage(): HTMLElement {
    const page = document.createElement("div");
    return page;
}

const page1 = setupPage();

describe("Simple Comprehension Quiz tests", () => {
    (window as any).acceptBloomAnalyticsData = undefined;
    (window as any).sendBloomAnalytics = undefined;
    it("does not crash when no analytics installed", () => {
        sendAnalytics(page1, true);
    });

    it("calls analytics and passes page and correct", () => {
        let acceptCalled = false;
        (window as any).acceptBloomAnalyticsData = (
            page,
            data,
            overwrite,
            callback
        ) => {
            acceptCalled = true;
            expect(page).toBe(page1);
            expect(data.correct).toBe(1);
            expect(overwrite).toBe(false);
        };
        (window as any).sendBloomAnalytics = () => {};

        sendAnalytics(page1, true);

        expect(acceptCalled).toBe(true);
    });

    it("calls sendAnalytics from callback", () => {
        let sendBloomAnalyticsCalled = false;
        (window as any).acceptBloomAnalyticsData = (
            page,
            data,
            overwrite,
            callback
        ) => {
            callback([
                { correct: 1 },
                { correct: 0 },
                { correct: 1 },
                { correct: 1 }
            ]);
        };
        (window as any).sendBloomAnalytics = (event, params) => {
            sendBloomAnalyticsCalled = true;
            expect(event).toBe("Questions correct");
            expect(params.questionCount).toBe(4);
            expect(params.rightFirstTime).toBe(3);
            expect(params.percentRight).toBe(75);
        };

        sendAnalytics(page1, true);

        expect(sendBloomAnalyticsCalled).toBe(true);
    });
});
