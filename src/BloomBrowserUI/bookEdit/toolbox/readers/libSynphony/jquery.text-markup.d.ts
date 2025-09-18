interface JQuery {
    checkLeveledReader(options: any): JQuery;
    checkDecodableReader(options: any): JQuery;
    getMaxSentenceLength(): number;
    getTotalWordCount(): number;
    removeSynphonyMarkup(): void;
}

interface JQueryStatic {
    markupGraphemes(
        word: string,
        gpcForm: string[],
        desiredGPCs?: string[],
    ): string;
}

declare var jQuery: JQueryStatic;
declare var $: JQueryStatic;
