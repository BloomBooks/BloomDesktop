import { getWithPromise, postJson } from "../utils/bloomApi";
import { AxiosResponse } from "axios";

export async function handleAITranslate(bookId: string) {
    try {
        // use collections/collectionProps to get the languagesToAiTranslate
        const aiLanguagesResponse = (await getWithPromise(
            `collections/collectionProps?book-id=${bookId}`
        )) as AxiosResponse<Record<string, string>[]>;

        if (aiLanguagesResponse?.data?.length === 0) return;

        const bookTextResponse = (await getWithPromise(
            `collections/book/json?book-id=${bookId}`
        )) as AxiosResponse<Record<string, string>[]>;

        console.log(
            "Response data:",
            JSON.stringify(bookTextResponse.data, null, 2)
        );

        if (!bookTextResponse?.data || !Array.isArray(bookTextResponse.data)) {
            console.error("Invalid response data format");
            return;
        }

        const texts = bookTextResponse.data;
        console.log("Translating text block:", JSON.stringify(texts, null, 2));
        // TODO: we will need to know which language the ai model was trained to translate from.
        // Could encode in the langtag, e.g. wsg-x-a-acts2-bloom10-from-en
        // In the case of Acts2, I have requested that they add this info to the API.
        // Else if L2 is one of the top world languages, then a reasonable guess would be that it was used for a Bible source during translation and thus training.

        const sourceLang = "en";
        const sourceText = texts[sourceLang];

        // Skip translation if source text is not available
        if (!sourceText) {
            console.warn(
                `No source text found for translation in language: ${sourceLang}`
            );
            return texts;
        }
        const translatedTexts = await getTranslations(
            sourceLang,
            "en-x-capitalized",
            texts
        );

        console.log(
            "Translated texts:",
            JSON.stringify(translatedTexts, null, 2)
        );
        // Post the translated content back using the json import endpoint
        await postJson(
            `collections/book/json?book-id=${bookId}`,
            translatedTexts
        );
        console.log("Mock translation completed");
    } catch (error) {
        console.error("Translation failed:", error);
    }
}

async function getTranslations(
    sourceLang: string,
    targetLangTag: string,
    texts: Record<string, string>[]
) {
    const translatedTexts = texts.map(text => {
        const translatedText = text[sourceLang].toUpperCase();
        return {
            ...text,
            [targetLangTag]: translatedText
        };
    });

    return translatedTexts;
}
