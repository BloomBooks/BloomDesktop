/// <reference path="typings/jquery/jquery.d.ts" />
import axios from "axios";
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
    static show(topic: string): boolean {
        axios.post('/bloom/help', topic);
        return false;
    }
}
