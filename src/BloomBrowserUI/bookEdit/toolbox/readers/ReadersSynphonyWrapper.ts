/// <reference path="libSynphony/synphony_lib.d.ts" />
/// <reference path="../../../typings/jquery/jquery.d.ts" />
/// <reference path="ReaderSettings.ts" />

import {
    theOneLanguageDataInstance,
    theOneLibSynphony,
    LanguageData
} from "./libSynphony/synphony_lib";
import { ReaderStage, ReaderLevel, ReaderSettings } from "./ReaderSettings";
import * as _ from "underscore";

export default class ReadersSynphonyWrapper {
    public stages: ReaderStage[] = [];
    public levels: ReaderLevel[] = [];
    public source: ReaderSettings;

    /**
     *
     * @param fileContent
     */
    public loadSettings(fileContent): void {
        //if (!lang_data) lang_data = new LanguageData(); now initialized in global declaration

        if (!fileContent) return;

        this.source = <ReaderSettings>(
            jQuery.extend(new ReaderSettings(), fileContent)
        );

        if (this.source.letters !== "") {
            theOneLanguageDataInstance.addGrapheme(
                this.source.letters.split(" ")
            );
            theOneLanguageDataInstance.addWord(
                this.source.moreWords.split(" ")
            );
            theOneLanguageDataInstance.LanguageSortOrder = this.source.letters.split(
                " "
            );
        }

        var stgs = this.source.stages;
        if (stgs) {
            this.stages = [];
            for (var j = 0; j < stgs.length; j++) {
                this.AddStage(
                    <ReaderStage>(
                        jQuery.extend(
                            true,
                            new ReaderStage((j + 1).toString()),
                            stgs[j]
                        )
                    )
                );
            }
        }

        var lvls = this.source.levels;
        if (lvls) {
            this.levels = [];
            for (var i = 0; i < lvls.length; i++) {
                this.addLevel(
                    <ReaderLevel>(
                        jQuery.extend(
                            true,
                            new ReaderLevel((i + 1).toString()),
                            lvls[i]
                        )
                    )
                );
            }
        }
        theOneLibSynphony.setExtraSentencePunctuation(
            this.source.sentencePunct
        );
    }

    public loadFromLangData(langData: LanguageData): void {
        if (!this.source) this.source = new ReaderSettings();

        if (this.source.letters === "") {
            var sorted = langData.LanguageSortOrder.join(" ")
                .toLowerCase()
                .split(" ");
            sorted = _.uniq(sorted);

            this.source.letters = sorted.join(" ");
        }
    }

    // This is at least useful for testing; maybe for real use.
    public AddStage(stage: ReaderStage): void {
        this.stages.push(stage);
    }

    /**
     * Add a list of words to the lang_data object
     * @param {Object} words The keys are the words, and the values are the counts
     */
    public static addWords(words: Object) {
        if (!words) return;

        var wordNames = Object.keys(words);

        //if (!lang_data) lang_data = new LanguageData(); now initialized in global declaration
        for (var i = 0; i < wordNames.length; i++) {
            theOneLanguageDataInstance.addWord(
                wordNames[i],
                words[wordNames[i]]
            );
        }
    }

    /**
     *
     * @param {int} [stageNumber] Optional. If present, returns all stages up to and including stageNumber. If missing, returns all stages.
     * @returns {ReaderStage[]} An array of ReaderStage objects
     */
    public getStages(stageNumber?: number): ReaderStage[] {
        if (typeof stageNumber === "undefined") return this.stages;
        else return _.first(this.stages, stageNumber);
    }

    public getLevels(): ReaderLevel[] {
        return this.levels;
    }

    public addLevel(aLevel: ReaderLevel): void {
        this.levels.push(aLevel);
    }
}
