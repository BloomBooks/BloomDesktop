/// <reference path="libsynphony/synphony.d.ts" />
/// <reference path="libsynphony/underscore-1.5.2.d.ts" />
/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="readerSettings.ts" />

declare function fireCSharpToolboxEvent(eventName: string, eventData: any);

class SynphonyApi {

  stages: ReaderStage[] = [];
  levels: ReaderLevel[] = [];
  source: ReaderSettings;

  /**
   *
   * @param fileContent
   */
  loadSettings(fileContent): void {

    if (!lang_data) lang_data = new LanguageData();

    if (!fileContent) return;

    this.source = <ReaderSettings>jQuery.extend(new ReaderSettings(), fileContent);

    if (this.source.letters !== '') {
      lang_data.addGrapheme(this.source.letters.split(' '));
      lang_data.addWord(this.source.moreWords.split(' '));
      lang_data.LanguageSortOrder = this.source.letters.split(' ');
    }

    var stgs = this.source.stages;
    if (stgs) {
      this.stages = [];
      for (var j = 0; j < stgs.length; j++) {
        this.AddStage(<ReaderStage>jQuery.extend(true, new ReaderStage((j+1).toString()), stgs[j]));
      }
    }

    var lvls = this.source.levels;
    if (lvls) {
      this.levels = [];
      for (var i = 0; i < lvls.length; i++) {
        this.addLevel(<ReaderLevel>jQuery.extend(true, new ReaderLevel((i+1).toString()), lvls[i]));
      }
    }
  }

  loadFromLangData(langData: LanguageData): void {

    if (!this.source) this.source = new ReaderSettings();

    if (this.source.letters === '') {

      var sorted = langData.LanguageSortOrder.join(' ').toLowerCase().split(' ');
      sorted = _.uniq(sorted);

      this.source.letters = sorted.join(' ');
    }
  }

  // This is at least useful for testing; maybe for real use.
  AddStage(stage: ReaderStage): void {
    this.stages.push(stage);
  }

  /**
   * Add a list of words to the lang_data object
   * @param {Object} words The keys are the words, and the values are the counts
   */
  static addWords(words: Object) {

    if (!words) return;

    var wordNames = Object.keys(words);

    if (!lang_data) lang_data = new LanguageData();
    for (var i = 0; i < wordNames.length; i++) {
      lang_data.addWord(wordNames[i], words[wordNames[i]]);
    }
  }

  /**
   *
   * @param {int} [stageNumber] Optional. If present, returns all stages up to and including stageNumber. If missing, returns all stages.
   * @returns {ReaderStage[]} An array of ReaderStage objects
   */
  getStages(stageNumber?: number): ReaderStage[] {

    if (typeof stageNumber === 'undefined')
      return this.stages;
    else
      return _.first(this.stages, stageNumber);
  }

  getLevels(): ReaderLevel[] {
    return this.levels;
  }

  addLevel(aLevel: ReaderLevel): void {
    this.levels.push(aLevel);
  }
}
