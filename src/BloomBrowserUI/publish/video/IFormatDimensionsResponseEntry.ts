/**
 * The response format for API calls asking for the dimensions of one or more formats
 *
 * Example: Used by the publish/av/getUpdatedFormatDimensions API call.
 *
 * @see FormatDimensionsResponseEntry in the C# code. (These two files should be kept in sync, otherwise the Typescript types will not match what you're actually getting in reality!)
 */
export interface IFormatDimensionsResponseEntry {
    format: string;
    aspectRatio: string;
    desiredWidth: number;
    desiredHeight: number;
    actualWidth: number;
    actualHeight: number;
}
