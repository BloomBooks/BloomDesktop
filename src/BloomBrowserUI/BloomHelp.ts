/// <reference path="typings/jquery/jquery.d.ts" />
import { postData } from "./utils/bloomApi";
/**
 * Class to with methods related to invoking bloom help
 * @constructor
 */
export default class BloomHelp {
    /**
     * Opens the application help topic
     * @param topic
     * @returns {boolean} Returns false to prevent navigation if link clicked.
     */
    public static show(topic: string): boolean {
        postData("help", topic);
        return false;
    }
}
