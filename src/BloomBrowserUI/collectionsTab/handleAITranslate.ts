import { getWithPromise, postJson } from "../utils/bloomApi";
import { AxiosResponse } from "axios";

export async function handleAITranslate(bookId: string) {
    try {
        // Get the book content as JSON using await
        const response = (await getWithPromise(
            `collections/book/json?book-id=${bookId}`
        )) as AxiosResponse<Record<string, string>[]>;

        console.log("Response data:", JSON.stringify(response.data, null, 2));

        if (!response?.data || !Array.isArray(response.data)) {
            console.error("Invalid response data format");
            return;
        }

        const texts = response.data;
        const translatedTexts = texts.map(textBlock => {
            if (typeof textBlock !== "object") {
                console.warn("Invalid text block format");
                return textBlock;
            }

            console.log(
                "Translating text block:",
                JSON.stringify(textBlock, null, 2)
            );
            const sourceLang = "en";
            const sourceText = textBlock[sourceLang];

            // Skip translation if source text is not available
            if (!sourceText) {
                console.warn(
                    `No source text found for translation in language: ${sourceLang}`
                );
                return textBlock;
            }

            // Do a mock "translation" - capitalize all letters
            const mockTranslation = sourceText.toUpperCase();

            // Add the new translation
            return {
                ...textBlock,
                "en-x-capitalized": mockTranslation
            };
        });

        console.log(
            "Translated texts:",
            JSON.stringify(translatedTexts, null, 2)
        );
        // Post the translated content back using the bulk import endpoint
        await postJson(
            `collections/book/json?book-id=${bookId}`,
            translatedTexts
        );
        console.log("Mock translation completed");
    } catch (error) {
        console.error("Translation failed:", error);
    }
}
