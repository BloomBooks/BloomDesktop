import { getMd5 } from "./md5Util";

describe("getMd5 tests", () => {
    it("calculates md5 on non-empty string", () => {
        expect(getMd5("Sentence 2.1၊ Sentence 2.2")).toBe(
            "ad15831c388fb93285cbb18306a4b734"
        );
    });

    it("calculates md5 on an empty string", () => {
        expect(getMd5("")).toBe("d41d8cd98f00b204e9800998ecf8427e");
    });
});
