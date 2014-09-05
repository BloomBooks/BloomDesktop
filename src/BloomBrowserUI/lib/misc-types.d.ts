/**
 * This file is for defining things that are not in .ts files, but the TypeScript compiler wants them.
 */

declare function GetInlineDictionary(): any;

interface String {
    startsWith(str: string): boolean
}