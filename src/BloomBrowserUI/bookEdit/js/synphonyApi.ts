/// <reference path="libsynphony/synphony.d.ts" />
/// <reference path="libsynphony/underscore-1.5.2.d.ts" />
/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="readerSettings.ts" />

declare function fireCSharpAccordionEvent(eventName: string, eventData: any);

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

    var data: ReaderSettings = <ReaderSettings>jQuery.extend(new ReaderSettings(), fileContent);

    this.source = data;

    if (data.letters !== '') {
      lang_data.addGrapheme(data.letters.split(' '));
      lang_data.addWord(data.moreWords.split(' '));
      lang_data.LanguageSortOrder = data.letters.split(' ');

      var stgs = data.stages;
      if (stgs) {
        this.stages = [];
        for (var j = 0; j < stgs.length; j++) {
          this.AddStage(<ReaderStage>jQuery.extend(true, new ReaderStage((j+1).toString()), stgs[j]));
        }
      }
    }

    var lvls = data.levels;
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

  static fireCSharpEvent(eventName: string, eventData: any) {
    fireCSharpAccordionEvent(eventName, eventData);
  }

  // This is at least useful for testing; maybe for real use.
  AddStage(stage: ReaderStage): void {
    this.stages.push(stage);
  }

  //noinspection JSUnusedGlobalSymbols
  /**
   * Gets a URI that points to the directory containing the "synphonyApi.js" file.
   * @returns {String}
   */
  getScriptDirectory(): string{

    var src = $('script[src$="synphonyApi.js"]').attr('src').replace('synphonyApi.js', '').replace(/\\/g, '/');
    if (!src) return '';
    return src;
  }

  /**
   * Add a list of words to the lang_data object
   * @param {Object} words The keys are the words, and the values are the counts
   */
  addWords(words: Object) {

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
