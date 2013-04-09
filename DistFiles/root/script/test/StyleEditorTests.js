TestCase("MyModule2", {
    "test s": function () {
        var editor = new MyModule.StyleEditor(document);
        assertEquals("", 1, $(document).find('#documentStyles').length);
    }
});
//@ sourceMappingURL=StyleEditorTests.js.map
