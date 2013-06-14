/* Load this script using conditional IE comments if you need to support IE 7 and IE 6. */

window.onload = function() {
	function addIcon(el, entity) {
		var html = el.innerHTML;
		el.innerHTML = '<span style="font-family: \'BloomIcons\'">' + entity + '</span>' + html;
	}
	var icons = {
			'bloom-icon-pushpin' : '&#xe000;',
			'bloom-icon-unlocked' : '&#xe001;',
			'bloom-icon-lock' : '&#xe002;',
			'bloom-icon-cog' : '&#xe003;',
			'bloom-icon-cogs' : '&#xe004;',
			'bloom-icon-cog-2' : '&#xe005;',
			'bloom-icon-equalizer' : '&#xe006;',
			'bloom-icon-lab' : '&#xe007;',
			'bloom-icon-remove' : '&#xe008;',
			'bloom-icon-remove-2' : '&#xe009;',
			'bloom-icon-list' : '&#xe00a;',
			'bloom-icon-flag' : '&#xe00b;',
			'bloom-icon-paragraph-left' : '&#xe00c;',
			'bloom-icon-paragraph-center' : '&#xe00d;',
			'bloom-icon-paragraph-right' : '&#xe00e;',
			'bloom-icon-paragraph-justify' : '&#xe00f;',
			'bloom-icon-FontSize' : '&#xe010;'
		},
		els = document.getElementsByTagName('*'),
		i, attr, html, c, el;
	for (i = 0; ; i += 1) {
		el = els[i];
		if(!el) {
			break;
		}
		attr = el.getAttribute('data-icon');
		if (attr) {
			addIcon(el, attr);
		}
		c = el.className;
		c = c.match(/bloom-icon-[^\s'"]+/);
		if (c && icons[c[0]]) {
			addIcon(el, icons[c[0]]);
		}
	}
};