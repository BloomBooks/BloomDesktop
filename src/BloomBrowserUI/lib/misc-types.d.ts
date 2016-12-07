/**
 * This file is for defining things that are not in .ts files, but the TypeScript compiler wants them.
 */

//declare function GetInlineDictionary(): any;
declare function WebFXTabPane(el: HTMLElement, bUseCookie?: boolean, selectFn?: (e: HTMLElement) => any): void;

interface String {
    startsWith(str: string): boolean
}

interface Window {
    postMessage(message: string, context: any): void;
}

interface longPressInterface extends JQuery {
    longPress(options?: any): JQuery;
}
