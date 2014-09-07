/// <reference path="libsynphony/synphony.d.ts" />
/// <reference path="libsynphony/underscore-1.5.2.d.ts" />
/// <reference path="../../lib/jquery.d.ts" />

/**
 * Decodable Leveled Reader Settings
 */
class DLRSettings {
	levels: Level[] = [];
	stages: Stage[] = [];
	letters: string = '';
	letterCombinations: string = '';
	moreWords: string = '';
}

class SynphonyApi {

	stages = [];
	levels = [];
	source: DLRSettings;

	/**
	 *
	 * @param fileContent
	 */
	loadSettings(fileContent): void {

		if (!lang_data) lang_data = new LanguageData();

		if (!fileContent) return;

		var data: DLRSettings = <DLRSettings>jQuery.extend(new DLRSettings(), fileContent);

		this.source = data;

		if (data.letters !== '') {
			lang_data.addGrapheme(data.letters.split(' '));
			lang_data.addGrapheme(data.letterCombinations.split(' '));
			lang_data.addWord(data.moreWords.split(' '));
			lang_data.LanguageSortOrder = data.letters.split(' ');

			var stgs = data.stages;
			if (stgs) {
				this.stages = [];
				for (var j = 0; j < stgs.length; j++) {
					this.AddStage(<Stage>jQuery.extend(true, new Stage((j+1).toString()), stgs[j]));
				}
			}
		}

		var lvls = data.levels;
		if (lvls) {
			this.levels = [];
			for (var i = 0; i < lvls.length; i++) {
				this.addLevel(<Level>jQuery.extend(true, new Level((i+1).toString()), lvls[i]));
			}
		}
	}

	loadFromLangData(langData: LanguageData): void {

		if (!this.source) this.source = new DLRSettings();

		if (this.source.letters === '') {

			var sorted = langData.LanguageSortOrder.join(' ').toLowerCase().split(' ');
			sorted = _.uniq(sorted);

			this.source.letters = sorted.join(' ');
		}
	}

	static fireCSharpEvent(eventName: string, eventData: any) {

		var event = new MessageEvent(eventName, {'view' : window, 'bubbles' : true, 'cancelable' : true, 'data' : eventData});
		document.dispatchEvent(event);
	}

	// This is at least useful for testing; maybe for real use.
	AddStage(stage: Stage): void {
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
	 * @returns {Stage[]} An array of Stage objects
	 */
	getStages(stageNumber: number): Stage[] {

		if (typeof stageNumber === 'undefined')
			return this.stages;
		else
			return _.first(this.stages, stageNumber);
	}

	getLevels(): Level[] {
		return this.levels;
	}

	addLevel(aLevel: Level): void {
		this.levels.push(aLevel);
	}
}


// Defines an object to hold data about one stage in the decodable books tool
class Stage {

	name: string;
	sightWords: string = '';

	constructor(name: string) {
		this.name = name;
	}

	getName(): string {
		return this.name;
	}
}

// Defines an object to hold data about one level in the leveled reader tool
class Level {

	name: string;
	thingsToRemember: string[] = [];

	// For each of these, 0 signifies unlimited.
	maxWordsPerPage: number = 0;
	maxWordsPerSentence: number = 0;
	maxWordsPerBook: number = 0;
	maxUniqueWordsPerBook: number = 0;

	constructor(name: string) {
		this.name = name;
	}

	getName(): string {
		return this.name;
	}

	getMaxWordsPerPage(): number {
		return this.maxWordsPerPage || 0;
	}

	getMaxWordsPerSentence(): number {
		return this.maxWordsPerSentence || 0;
	}

	getMaxWordsPerBook(): number {
		return this.maxWordsPerBook || 0;
	}

	getMaxUniqueWordsPerBook(): number {
		return this.maxUniqueWordsPerBook || 0;
	}
}
