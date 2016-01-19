describe("Localization Manager tests", function() {

    it("simpleDotNetFormat test", function() {

        var result = SimpleDotNetFormat('{0} {1} {2}', ['a', 'b', 'c']);
        expect(result).toEqual('a b c');

        result = SimpleDotNetFormat('{0} {1} {0}', ['a', 'b', 'c']);
        expect(result).toEqual('a b a');

        result = SimpleDotNetFormat('{0} {1} {2} {3}', ['a', 'b', 'c']);
        expect(result).toEqual('a b c {3}');

        result = SimpleDotNetFormat('{0} {1} {2}', ['{1}', 'b', 'c']);
        expect(result).toEqual('{1} b c');
    });
});