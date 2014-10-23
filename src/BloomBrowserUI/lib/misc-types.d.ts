/**
 * This file is for defining things that are not in .ts files, but the TypeScript compiler wants them.
 */

declare function GetInlineDictionary(): any;

interface String {
	startsWith(str: string): boolean
}

interface Window {
	postMessage(message: string, context: any): void;
}

interface longPressInterface extends JQuery {
	longPress(options?: any): JQuery;
}