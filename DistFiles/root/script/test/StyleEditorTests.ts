///<reference path="../src/StyleEditing.ts"/>


TestCase("MyModule2",
	{"test s": ()=> {

//        $('body').append('<div id="blah">blaaah</div>');


		var editor = new MyModule.StyleEditor(document);
		assertEquals("",1,$(document).find('#documentStyles').length);
	}}
);
