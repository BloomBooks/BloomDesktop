// MooTools: the javascript framework.
// Load this file's selection again by visiting: http://mootools.net/more/a83532a2c88f315d818ea9cbe8a82f8f
// Or build this file again with packager using: packager build More/Date More/Date.Extras More/OverText More/Locale More/Locale.en-US.Date More/Locale.es-ES.Date More/Locale.fr-FR.Date More/Locale.he-IL.Date More/Locale.zh-CH.Date
/*
---

script: More.js

name: More

description: MooTools More

license: MIT-style license

authors:
  - Guillermo Rauch
  - Thomas Aylott
  - Scott Kyle
  - Arian Stolwijk
  - Tim Wienk
  - Christoph Pojer
  - Aaron Newton
  - Jacob Thornton

requires:
  - Core/MooTools

provides: [MooTools.More]

...
*/

MooTools.More = {
  'version': '1.4.0.1',
  'build': 'a4244edf2aa97ac8a196fc96082dd35af1abab87'
};


/*
---

script: Object.Extras.js

name: Object.Extras

description: Extra Object generics, like getFromPath which allows a path notation to child elements.

license: MIT-style license

authors:
  - Aaron Newton

requires:
  - Core/Object
  - /MooTools.More

provides: [Object.Extras]

...
*/

(function(){

var defined = function(value){
  return value != null;
};

var hasOwnProperty = Object.prototype.hasOwnProperty;

Object.extend({

  getFromPath: function(source, parts){
    if (typeof parts == 'string') parts = parts.split('.');
    for (var i = 0, l = parts.length; i < l; i++){
      if (hasOwnProperty.call(source, parts[i])) source = source[parts[i]];
      else return null;
    }
    return source;
  },

  cleanValues: function(object, method){
    method = method || defined;
    for (var key in object) if (!method(object[key])){
      delete object[key];
    }
    return object;
  },

  erase: function(object, key){
    if (hasOwnProperty.call(object, key)) delete object[key];
    return object;
  },

  run: function(object){
    var args = Array.slice(arguments, 1);
    for (var key in object) if (object[key].apply){
      object[key].apply(object, args);
    }
    return object;
  }

});

})();


/*
---

script: Locale.js

name: Locale

description: Provides methods for localization.

license: MIT-style license

authors:
  - Aaron Newton
  - Arian Stolwijk

requires:
  - Core/Events
  - /Object.Extras
  - /MooTools.More

provides: [Locale, Lang]

...
*/

(function(){

var current = null,
  locales = {},
  inherits = {};

var getSet = function(set){
  if (instanceOf(set, Locale.Set)) return set;
  else return locales[set];
};

var Locale = this.Locale = {

  define: function(locale, set, key, value){
    var name;
    if (instanceOf(locale, Locale.Set)){
      name = locale.name;
      if (name) locales[name] = locale;
    } else {
      name = locale;
      if (!locales[name]) locales[name] = new Locale.Set(name);
      locale = locales[name];
    }

    if (set) locale.define(set, key, value);



    if (!current) current = locale;

    return locale;
  },

  use: function(locale){
    locale = getSet(locale);

    if (locale){
      current = locale;

      this.fireEvent('change', locale);


    }

    return this;
  },

  getCurrent: function(){
    return current;
  },

  get: function(key, args){
    return (current) ? current.get(key, args) : '';
  },

  inherit: function(locale, inherits, set){
    locale = getSet(locale);

    if (locale) locale.inherit(inherits, set);
    return this;
  },

  list: function(){
    return Object.keys(locales);
  }

};

Object.append(Locale, new Events);

Locale.Set = new Class({

  sets: {},

  inherits: {
    locales: [],
    sets: {}
  },

  initialize: function(name){
    this.name = name || '';
  },

  define: function(set, key, value){
    var defineData = this.sets[set];
    if (!defineData) defineData = {};

    if (key){
      if (typeOf(key) == 'object') defineData = Object.merge(defineData, key);
      else defineData[key] = value;
    }
    this.sets[set] = defineData;

    return this;
  },

  get: function(key, args, _base){
    var value = Object.getFromPath(this.sets, key);
    if (value != null){
      var type = typeOf(value);
      if (type == 'function') value = value.apply(null, Array.from(args));
      else if (type == 'object') value = Object.clone(value);
      return value;
    }

    // get value of inherited locales
    var index = key.indexOf('.'),
      set = index < 0 ? key : key.substr(0, index),
      names = (this.inherits.sets[set] || []).combine(this.inherits.locales).include('en-US');
    if (!_base) _base = [];

    for (var i = 0, l = names.length; i < l; i++){
      if (_base.contains(names[i])) continue;
      _base.include(names[i]);

      var locale = locales[names[i]];
      if (!locale) continue;

      value = locale.get(key, args, _base);
      if (value != null) return value;
    }

    return '';
  },

  inherit: function(names, set){
    names = Array.from(names);

    if (set && !this.inherits.sets[set]) this.inherits.sets[set] = [];

    var l = names.length;
    while (l--) (set ? this.inherits.sets[set] : this.inherits.locales).unshift(names[l]);

    return this;
  }

});



})();


/*
---

name: Locale.en-US.Date

description: Date messages for US English.

license: MIT-style license

authors:
  - Aaron Newton

requires:
  - /Locale

provides: [Locale.en-US.Date]

...
*/

Locale.define('en-US', 'Date', {

  months: ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'],
  months_abbr: ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'],
  days: ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'],
  days_abbr: ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'],

  // Culture's date order: MM/DD/YYYY
  dateOrder: ['month', 'date', 'year'],
  shortDate: '%m/%d/%Y',
  shortTime: '%I:%M%p',
  AM: 'AM',
  PM: 'PM',
  firstDayOfWeek: 0,

  // Date.Extras
  ordinal: function(dayOfMonth){
    // 1st, 2nd, 3rd, etc.
    return (dayOfMonth > 3 && dayOfMonth < 21) ? 'th' : ['th', 'st', 'nd', 'rd', 'th'][Math.min(dayOfMonth % 10, 4)];
  },

  lessThanMinuteAgo: 'less than a minute ago',
  minuteAgo: 'about a minute ago',
  minutesAgo: '{delta} minutes ago',
  hourAgo: 'about an hour ago',
  hoursAgo: 'about {delta} hours ago',
  dayAgo: '1 day ago',
  daysAgo: '{delta} days ago',
  weekAgo: '1 week ago',
  weeksAgo: '{delta} weeks ago',
  monthAgo: '1 month ago',
  monthsAgo: '{delta} months ago',
  yearAgo: '1 year ago',
  yearsAgo: '{delta} years ago',

  lessThanMinuteUntil: 'less than a minute from now',
  minuteUntil: 'about a minute from now',
  minutesUntil: '{delta} minutes from now',
  hourUntil: 'about an hour from now',
  hoursUntil: 'about {delta} hours from now',
  dayUntil: '1 day from now',
  daysUntil: '{delta} days from now',
  weekUntil: '1 week from now',
  weeksUntil: '{delta} weeks from now',
  monthUntil: '1 month from now',
  monthsUntil: '{delta} months from now',
  yearUntil: '1 year from now',
  yearsUntil: '{delta} years from now'

});


/*
---

script: Date.js

name: Date

description: Extends the Date native object to include methods useful in managing dates.

license: MIT-style license

authors:
  - Aaron Newton
  - Nicholas Barthelemy - https://svn.nbarthelemy.com/date-js/
  - Harald Kirshner - mail [at] digitarald.de; http://digitarald.de
  - Scott Kyle - scott [at] appden.com; http://appden.com

requires:
  - Core/Array
  - Core/String
  - Core/Number
  - MooTools.More
  - Locale
  - Locale.en-US.Date

provides: [Date]

...
*/

(function(){

var Date = this.Date;

var DateMethods = Date.Methods = {
  ms: 'Milliseconds',
  year: 'FullYear',
  min: 'Minutes',
  mo: 'Month',
  sec: 'Seconds',
  hr: 'Hours'
};

['Date', 'Day', 'FullYear', 'Hours', 'Milliseconds', 'Minutes', 'Month', 'Seconds', 'Time', 'TimezoneOffset',
  'Week', 'Timezone', 'GMTOffset', 'DayOfYear', 'LastMonth', 'LastDayOfMonth', 'UTCDate', 'UTCDay', 'UTCFullYear',
  'AMPM', 'Ordinal', 'UTCHours', 'UTCMilliseconds', 'UTCMinutes', 'UTCMonth', 'UTCSeconds', 'UTCMilliseconds'].each(function(method){
  Date.Methods[method.toLowerCase()] = method;
});

var pad = function(n, digits, string){
  if (digits == 1) return n;
  return n < Math.pow(10, digits - 1) ? (string || '0') + pad(n, digits - 1, string) : n;
};

Date.implement({

  set: function(prop, value){
    prop = prop.toLowerCase();
    var method = DateMethods[prop] && 'set' + DateMethods[prop];
    if (method && this[method]) this[method](value);
    return this;
  }.overloadSetter(),

  get: function(prop){
    prop = prop.toLowerCase();
    var method = DateMethods[prop] && 'get' + DateMethods[prop];
    if (method && this[method]) return this[method]();
    return null;
  }.overloadGetter(),

  clone: function(){
    return new Date(this.get('time'));
  },

  increment: function(interval, times){
    interval = interval || 'day';
    times = times != null ? times : 1;

    switch (interval){
      case 'year':
        return this.increment('month', times * 12);
      case 'month':
        var d = this.get('date');
        this.set('date', 1).set('mo', this.get('mo') + times);
        return this.set('date', d.min(this.get('lastdayofmonth')));
      case 'week':
        return this.increment('day', times * 7);
      case 'day':
        return this.set('date', this.get('date') + times);
    }

    if (!Date.units[interval]) throw new Error(interval + ' is not a supported interval');

    return this.set('time', this.get('time') + times * Date.units[interval]());
  },

  decrement: function(interval, times){
    return this.increment(interval, -1 * (times != null ? times : 1));
  },

  isLeapYear: function(){
    return Date.isLeapYear(this.get('year'));
  },

  clearTime: function(){
    return this.set({hr: 0, min: 0, sec: 0, ms: 0});
  },

  diff: function(date, resolution){
    if (typeOf(date) == 'string') date = Date.parse(date);

    return ((date - this) / Date.units[resolution || 'day'](3, 3)).round(); // non-leap year, 30-day month
  },

  getLastDayOfMonth: function(){
    return Date.daysInMonth(this.get('mo'), this.get('year'));
  },

  getDayOfYear: function(){
    return (Date.UTC(this.get('year'), this.get('mo'), this.get('date') + 1)
      - Date.UTC(this.get('year'), 0, 1)) / Date.units.day();
  },

  setDay: function(day, firstDayOfWeek){
    if (firstDayOfWeek == null){
      firstDayOfWeek = Date.getMsg('firstDayOfWeek');
      if (firstDayOfWeek === '') firstDayOfWeek = 1;
    }

    day = (7 + Date.parseDay(day, true) - firstDayOfWeek) % 7;
    var currentDay = (7 + this.get('day') - firstDayOfWeek) % 7;

    return this.increment('day', day - currentDay);
  },

  getWeek: function(firstDayOfWeek){
    if (firstDayOfWeek == null){
      firstDayOfWeek = Date.getMsg('firstDayOfWeek');
      if (firstDayOfWeek === '') firstDayOfWeek = 1;
    }

    var date = this,
      dayOfWeek = (7 + date.get('day') - firstDayOfWeek) % 7,
      dividend = 0,
      firstDayOfYear;

    if (firstDayOfWeek == 1){
      // ISO-8601, week belongs to year that has the most days of the week (i.e. has the thursday of the week)
      var month = date.get('month'),
        startOfWeek = date.get('date') - dayOfWeek;

      if (month == 11 && startOfWeek > 28) return 1; // Week 1 of next year

      if (month == 0 && startOfWeek < -2){
        // Use a date from last year to determine the week
        date = new Date(date).decrement('day', dayOfWeek);
        dayOfWeek = 0;
      }

      firstDayOfYear = new Date(date.get('year'), 0, 1).get('day') || 7;
      if (firstDayOfYear > 4) dividend = -7; // First week of the year is not week 1
    } else {
      // In other cultures the first week of the year is always week 1 and the last week always 53 or 54.
      // Days in the same week can have a different weeknumber if the week spreads across two years.
      firstDayOfYear = new Date(date.get('year'), 0, 1).get('day');
    }

    dividend += date.get('dayofyear');
    dividend += 6 - dayOfWeek; // Add days so we calculate the current date's week as a full week
    dividend += (7 + firstDayOfYear - firstDayOfWeek) % 7; // Make up for first week of the year not being a full week

    return (dividend / 7);
  },

  getOrdinal: function(day){
    return Date.getMsg('ordinal', day || this.get('date'));
  },

  getTimezone: function(){
    return this.toString()
      .replace(/^.*? ([A-Z]{3}).[0-9]{4}.*$/, '$1')
      .replace(/^.*?\(([A-Z])[a-z]+ ([A-Z])[a-z]+ ([A-Z])[a-z]+\)$/, '$1$2$3');
  },

  getGMTOffset: function(){
    var off = this.get('timezoneOffset');
    return ((off > 0) ? '-' : '+') + pad((off.abs() / 60).floor(), 2) + pad(off % 60, 2);
  },

  setAMPM: function(ampm){
    ampm = ampm.toUpperCase();
    var hr = this.get('hr');
    if (hr > 11 && ampm == 'AM') return this.decrement('hour', 12);
    else if (hr < 12 && ampm == 'PM') return this.increment('hour', 12);
    return this;
  },

  getAMPM: function(){
    return (this.get('hr') < 12) ? 'AM' : 'PM';
  },

  parse: function(str){
    this.set('time', Date.parse(str));
    return this;
  },

  isValid: function(date){
    if (!date) date = this;
    return typeOf(date) == 'date' && !isNaN(date.valueOf());
  },

  format: function(format){
    if (!this.isValid()) return 'invalid date';

    if (!format) format = '%x %X';
    if (typeof format == 'string') format = formats[format.toLowerCase()] || format;
    if (typeof format == 'function') return format(this);

    var d = this;
    return format.replace(/%([a-z%])/gi,
      function($0, $1){
        switch ($1){
          case 'a': return Date.getMsg('days_abbr')[d.get('day')];
          case 'A': return Date.getMsg('days')[d.get('day')];
          case 'b': return Date.getMsg('months_abbr')[d.get('month')];
          case 'B': return Date.getMsg('months')[d.get('month')];
          case 'c': return d.format('%a %b %d %H:%M:%S %Y');
          case 'd': return pad(d.get('date'), 2);
          case 'e': return pad(d.get('date'), 2, ' ');
          case 'H': return pad(d.get('hr'), 2);
          case 'I': return pad((d.get('hr') % 12) || 12, 2);
          case 'j': return pad(d.get('dayofyear'), 3);
          case 'k': return pad(d.get('hr'), 2, ' ');
          case 'l': return pad((d.get('hr') % 12) || 12, 2, ' ');
          case 'L': return pad(d.get('ms'), 3);
          case 'm': return pad((d.get('mo') + 1), 2);
          case 'M': return pad(d.get('min'), 2);
          case 'o': return d.get('ordinal');
          case 'p': return Date.getMsg(d.get('ampm'));
          case 's': return Math.round(d / 1000);
          case 'S': return pad(d.get('seconds'), 2);
          case 'T': return d.format('%H:%M:%S');
          case 'U': return pad(d.get('week'), 2);
          case 'w': return d.get('day');
          case 'x': return d.format(Date.getMsg('shortDate'));
          case 'X': return d.format(Date.getMsg('shortTime'));
          case 'y': return d.get('year').toString().substr(2);
          case 'Y': return d.get('year');
          case 'z': return d.get('GMTOffset');
          case 'Z': return d.get('Timezone');
        }
        return $1;
      }
    );
  },

  toISOString: function(){
    return this.format('iso8601');
  }

}).alias({
  toJSON: 'toISOString',
  compare: 'diff',
  strftime: 'format'
});

// The day and month abbreviations are standardized, so we cannot use simply %a and %b because they will get localized
var rfcDayAbbr = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'],
  rfcMonthAbbr = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

var formats = {
  db: '%Y-%m-%d %H:%M:%S',
  compact: '%Y%m%dT%H%M%S',
  'short': '%d %b %H:%M',
  'long': '%B %d, %Y %H:%M',
  rfc822: function(date){
    return rfcDayAbbr[date.get('day')] + date.format(', %d ') + rfcMonthAbbr[date.get('month')] + date.format(' %Y %H:%M:%S %Z');
  },
  rfc2822: function(date){
    return rfcDayAbbr[date.get('day')] + date.format(', %d ') + rfcMonthAbbr[date.get('month')] + date.format(' %Y %H:%M:%S %z');
  },
  iso8601: function(date){
    return (
      date.getUTCFullYear() + '-' +
      pad(date.getUTCMonth() + 1, 2) + '-' +
      pad(date.getUTCDate(), 2) + 'T' +
      pad(date.getUTCHours(), 2) + ':' +
      pad(date.getUTCMinutes(), 2) + ':' +
      pad(date.getUTCSeconds(), 2) + '.' +
      pad(date.getUTCMilliseconds(), 3) + 'Z'
    );
  }
};

var parsePatterns = [],
  nativeParse = Date.parse;

var parseWord = function(type, word, num){
  var ret = -1,
    translated = Date.getMsg(type + 's');
  switch (typeOf(word)){
    case 'object':
      ret = translated[word.get(type)];
      break;
    case 'number':
      ret = translated[word];
      if (!ret) throw new Error('Invalid ' + type + ' index: ' + word);
      break;
    case 'string':
      var match = translated.filter(function(name){
        return this.test(name);
      }, new RegExp('^' + word, 'i'));
      if (!match.length) throw new Error('Invalid ' + type + ' string');
      if (match.length > 1) throw new Error('Ambiguous ' + type);
      ret = match[0];
  }

  return (num) ? translated.indexOf(ret) : ret;
};

var startCentury = 1900,
  startYear = 70;

Date.extend({

  getMsg: function(key, args){
    return Locale.get('Date.' + key, args);
  },

  units: {
    ms: Function.from(1),
    second: Function.from(1000),
    minute: Function.from(60000),
    hour: Function.from(3600000),
    day: Function.from(86400000),
    week: Function.from(608400000),
    month: function(month, year){
      var d = new Date;
      return Date.daysInMonth(month != null ? month : d.get('mo'), year != null ? year : d.get('year')) * 86400000;
    },
    year: function(year){
      year = year || new Date().get('year');
      return Date.isLeapYear(year) ? 31622400000 : 31536000000;
    }
  },

  daysInMonth: function(month, year){
    return [31, Date.isLeapYear(year) ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31][month];
  },

  isLeapYear: function(year){
    return ((year % 4 === 0) && (year % 100 !== 0)) || (year % 400 === 0);
  },

  parse: function(from){
    var t = typeOf(from);
    if (t == 'number') return new Date(from);
    if (t != 'string') return from;
    from = from.clean();
    if (!from.length) return null;

    var parsed;
    parsePatterns.some(function(pattern){
      var bits = pattern.re.exec(from);
      return (bits) ? (parsed = pattern.handler(bits)) : false;
    });

    if (!(parsed && parsed.isValid())){
      parsed = new Date(nativeParse(from));
      if (!(parsed && parsed.isValid())) parsed = new Date(from.toInt());
    }
    return parsed;
  },

  parseDay: function(day, num){
    return parseWord('day', day, num);
  },

  parseMonth: function(month, num){
    return parseWord('month', month, num);
  },

  parseUTC: function(value){
    var localDate = new Date(value);
    var utcSeconds = Date.UTC(
      localDate.get('year'),
      localDate.get('mo'),
      localDate.get('date'),
      localDate.get('hr'),
      localDate.get('min'),
      localDate.get('sec'),
      localDate.get('ms')
    );
    return new Date(utcSeconds);
  },

  orderIndex: function(unit){
    return Date.getMsg('dateOrder').indexOf(unit) + 1;
  },

  defineFormat: function(name, format){
    formats[name] = format;
    return this;
  },



  defineParser: function(pattern){
    parsePatterns.push((pattern.re && pattern.handler) ? pattern : build(pattern));
    return this;
  },

  defineParsers: function(){
    Array.flatten(arguments).each(Date.defineParser);
    return this;
  },

  define2DigitYearStart: function(year){
    startYear = year % 100;
    startCentury = year - startYear;
    return this;
  }

}).extend({
  defineFormats: Date.defineFormat.overloadSetter()
});

var regexOf = function(type){
  return new RegExp('(?:' + Date.getMsg(type).map(function(name){
    return name.substr(0, 3);
  }).join('|') + ')[a-z]*');
};

var replacers = function(key){
  switch (key){
    case 'T':
      return '%H:%M:%S';
    case 'x': // iso8601 covers yyyy-mm-dd, so just check if month is first
      return ((Date.orderIndex('month') == 1) ? '%m[-./]%d' : '%d[-./]%m') + '([-./]%y)?';
    case 'X':
      return '%H([.:]%M)?([.:]%S([.:]%s)?)? ?%p? ?%z?';
  }
  return null;
};

var keys = {
  d: /[0-2]?[0-9]|3[01]/,
  H: /[01]?[0-9]|2[0-3]/,
  I: /0?[1-9]|1[0-2]/,
  M: /[0-5]?\d/,
  s: /\d+/,
  o: /[a-z]*/,
  p: /[ap]\.?m\.?/,
  y: /\d{2}|\d{4}/,
  Y: /\d{4}/,
  z: /Z|[+-]\d{2}(?::?\d{2})?/
};

keys.m = keys.I;
keys.S = keys.M;

var currentLanguage;

var recompile = function(language){
  currentLanguage = language;

  keys.a = keys.A = regexOf('days');
  keys.b = keys.B = regexOf('months');

  parsePatterns.each(function(pattern, i){
    if (pattern.format) parsePatterns[i] = build(pattern.format);
  });
};

var build = function(format){
  if (!currentLanguage) return {format: format};

  var parsed = [];
  var re = (format.source || format) // allow format to be regex
   .replace(/%([a-z])/gi,
    function($0, $1){
      return replacers($1) || $0;
    }
  ).replace(/\((?!\?)/g, '(?:') // make all groups non-capturing
   .replace(/ (?!\?|\*)/g, ',? ') // be forgiving with spaces and commas
   .replace(/%([a-z%])/gi,
    function($0, $1){
      var p = keys[$1];
      if (!p) return $1;
      parsed.push($1);
      return '(' + p.source + ')';
    }
  ).replace(/\[a-z\]/gi, '[a-z\\u00c0-\\uffff;\&]'); // handle unicode words

  return {
    format: format,
    re: new RegExp('^' + re + '$', 'i'),
    handler: function(bits){
      bits = bits.slice(1).associate(parsed);
      var date = new Date().clearTime(),
        year = bits.y || bits.Y;

      if (year != null) handle.call(date, 'y', year); // need to start in the right year
      if ('d' in bits) handle.call(date, 'd', 1);
      if ('m' in bits || bits.b || bits.B) handle.call(date, 'm', 1);

      for (var key in bits) handle.call(date, key, bits[key]);
      return date;
    }
  };
};

var handle = function(key, value){
  if (!value) return this;

  switch (key){
    case 'a': case 'A': return this.set('day', Date.parseDay(value, true));
    case 'b': case 'B': return this.set('mo', Date.parseMonth(value, true));
    case 'd': return this.set('date', value);
    case 'H': case 'I': return this.set('hr', value);
    case 'm': return this.set('mo', value - 1);
    case 'M': return this.set('min', value);
    case 'p': return this.set('ampm', value.replace(/\./g, ''));
    case 'S': return this.set('sec', value);
    case 's': return this.set('ms', ('0.' + value) * 1000);
    case 'w': return this.set('day', value);
    case 'Y': return this.set('year', value);
    case 'y':
      value = +value;
      if (value < 100) value += startCentury + (value < startYear ? 100 : 0);
      return this.set('year', value);
    case 'z':
      if (value == 'Z') value = '+00';
      var offset = value.match(/([+-])(\d{2}):?(\d{2})?/);
      offset = (offset[1] + '1') * (offset[2] * 60 + (+offset[3] || 0)) + this.getTimezoneOffset();
      return this.set('time', this - offset * 60000);
  }

  return this;
};

Date.defineParsers(
  '%Y([-./]%m([-./]%d((T| )%X)?)?)?', // "1999-12-31", "1999-12-31 11:59pm", "1999-12-31 23:59:59", ISO8601
  '%Y%m%d(T%H(%M%S?)?)?', // "19991231", "19991231T1159", compact
  '%x( %X)?', // "12/31", "12.31.99", "12-31-1999", "12/31/2008 11:59 PM"
  '%d%o( %b( %Y)?)?( %X)?', // "31st", "31st December", "31 Dec 1999", "31 Dec 1999 11:59pm"
  '%b( %d%o)?( %Y)?( %X)?', // Same as above with month and day switched
  '%Y %b( %d%o( %X)?)?', // Same as above with year coming first
  '%o %b %d %X %z %Y', // "Thu Oct 22 08:11:23 +0000 2009"
  '%T', // %H:%M:%S
  '%H:%M( ?%p)?' // "11:05pm", "11:05 am" and "11:05"
);

Locale.addEvent('change', function(language){
  if (Locale.get('Date')) recompile(language);
}).fireEvent('change', Locale.getCurrent());

})();


/*
---

script: Date.Extras.js

name: Date.Extras

description: Extends the Date native object to include extra methods (on top of those in Date.js).

license: MIT-style license

authors:
  - Aaron Newton
  - Scott Kyle

requires:
  - /Date

provides: [Date.Extras]

...
*/

Date.implement({

  timeDiffInWords: function(to){
    return Date.distanceOfTimeInWords(this, to || new Date);
  },

  timeDiff: function(to, separator){
    if (to == null) to = new Date;
    var delta = ((to - this) / 1000).floor().abs();

    var vals = [],
      durations = [60, 60, 24, 365, 0],
      names = ['s', 'm', 'h', 'd', 'y'],
      value, duration;

    for (var item = 0; item < durations.length; item++){
      if (item && !delta) break;
      value = delta;
      if ((duration = durations[item])){
        value = (delta % duration);
        delta = (delta / duration).floor();
      }
      vals.unshift(value + (names[item] || ''));
    }

    return vals.join(separator || ':');
  }

}).extend({

  distanceOfTimeInWords: function(from, to){
    return Date.getTimePhrase(((to - from) / 1000).toInt());
  },

  getTimePhrase: function(delta){
    var suffix = (delta < 0) ? 'Until' : 'Ago';
    if (delta < 0) delta *= -1;

    var units = {
      minute: 60,
      hour: 60,
      day: 24,
      week: 7,
      month: 52 / 12,
      year: 12,
      eon: Infinity
    };

    var msg = 'lessThanMinute';

    for (var unit in units){
      var interval = units[unit];
      if (delta < 1.5 * interval){
        if (delta > 0.75 * interval) msg = unit;
        break;
      }
      delta /= interval;
      msg = unit + 's';
    }

    delta = delta.round();
    return Date.getMsg(msg + suffix, delta).substitute({delta: delta});
  }

}).defineParsers(

  {
    // "today", "tomorrow", "yesterday"
    re: /^(?:tod|tom|yes)/i,
    handler: function(bits){
      var d = new Date().clearTime();
      switch (bits[0]){
        case 'tom': return d.increment();
        case 'yes': return d.decrement();
        default: return d;
      }
    }
  },

  {
    // "next Wednesday", "last Thursday"
    re: /^(next|last) ([a-z]+)$/i,
    handler: function(bits){
      var d = new Date().clearTime();
      var day = d.getDay();
      var newDay = Date.parseDay(bits[2], true);
      var addDays = newDay - day;
      if (newDay <= day) addDays += 7;
      if (bits[1] == 'last') addDays -= 7;
      return d.set('date', d.getDate() + addDays);
    }
  }

).alias('timeAgoInWords', 'timeDiffInWords');


/*
---

script: Class.Binds.js

name: Class.Binds

description: Automagically binds specified methods in a class to the instance of the class.

license: MIT-style license

authors:
  - Aaron Newton

requires:
  - Core/Class
  - /MooTools.More

provides: [Class.Binds]

...
*/

Class.Mutators.Binds = function(binds){
  if (!this.prototype.initialize) this.implement('initialize', function(){});
  return Array.from(binds).concat(this.prototype.Binds || []);
};

Class.Mutators.initialize = function(initialize){
  return function(){
    Array.from(this.Binds).each(function(name){
      var original = this[name];
      if (original) this[name] = original.bind(this);
    }, this);
    return initialize.apply(this, arguments);
  };
};


/*
---

script: Class.Occlude.js

name: Class.Occlude

description: Prevents a class from being applied to a DOM element twice.

license: MIT-style license.

authors:
  - Aaron Newton

requires:
  - Core/Class
  - Core/Element
  - /MooTools.More

provides: [Class.Occlude]

...
*/

Class.Occlude = new Class({

  occlude: function(property, element){
    element = document.id(element || this.element);
    var instance = element.retrieve(property || this.property);
    if (instance && !this.occluded)
      return (this.occluded = instance);

    this.occluded = false;
    element.store(property || this.property, this);
    return this.occluded;
  }

});


/*
---

script: Element.Measure.js

name: Element.Measure

description: Extends the Element native object to include methods useful in measuring dimensions.

credits: "Element.measure / .expose methods by Daniel Steigerwald License: MIT-style license. Copyright: Copyright (c) 2008 Daniel Steigerwald, daniel.steigerwald.cz"

license: MIT-style license

authors:
  - Aaron Newton

requires:
  - Core/Element.Style
  - Core/Element.Dimensions
  - /MooTools.More

provides: [Element.Measure]

...
*/

(function(){

var getStylesList = function(styles, planes){
  var list = [];
  Object.each(planes, function(directions){
    Object.each(directions, function(edge){
      styles.each(function(style){
        list.push(style + '-' + edge + (style == 'border' ? '-width' : ''));
      });
    });
  });
  return list;
};

var calculateEdgeSize = function(edge, styles){
  var total = 0;
  Object.each(styles, function(value, style){
    if (style.test(edge)) total = total + value.toInt();
  });
  return total;
};

var isVisible = function(el){
  return !!(!el || el.offsetHeight || el.offsetWidth);
};


Element.implement({

  measure: function(fn){
    if (isVisible(this)) return fn.call(this);
    var parent = this.getParent(),
      toMeasure = [];
    while (!isVisible(parent) && parent != document.body){
      toMeasure.push(parent.expose());
      parent = parent.getParent();
    }
    var restore = this.expose(),
      result = fn.call(this);
    restore();
    toMeasure.each(function(restore){
      restore();
    });
    return result;
  },

  expose: function(){
    if (this.getStyle('display') != 'none') return function(){};
    var before = this.style.cssText;
    this.setStyles({
      display: 'block',
      position: 'absolute',
      visibility: 'hidden'
    });
    return function(){
      this.style.cssText = before;
    }.bind(this);
  },

  getDimensions: function(options){
    options = Object.merge({computeSize: false}, options);
    var dim = {x: 0, y: 0};

    var getSize = function(el, options){
      return (options.computeSize) ? el.getComputedSize(options) : el.getSize();
    };

    var parent = this.getParent('body');

    if (parent && this.getStyle('display') == 'none'){
      dim = this.measure(function(){
        return getSize(this, options);
      });
    } else if (parent){
      try { //safari sometimes crashes here, so catch it
        dim = getSize(this, options);
      }catch(e){}
    }

    return Object.append(dim, (dim.x || dim.x === 0) ? {
        width: dim.x,
        height: dim.y
      } : {
        x: dim.width,
        y: dim.height
      }
    );
  },

  getComputedSize: function(options){


    options = Object.merge({
      styles: ['padding','border'],
      planes: {
        height: ['top','bottom'],
        width: ['left','right']
      },
      mode: 'both'
    }, options);

    var styles = {},
      size = {width: 0, height: 0},
      dimensions;

    if (options.mode == 'vertical'){
      delete size.width;
      delete options.planes.width;
    } else if (options.mode == 'horizontal'){
      delete size.height;
      delete options.planes.height;
    }

    getStylesList(options.styles, options.planes).each(function(style){
      styles[style] = this.getStyle(style).toInt();
    }, this);

    Object.each(options.planes, function(edges, plane){

      var capitalized = plane.capitalize(),
        style = this.getStyle(plane);

      if (style == 'auto' && !dimensions) dimensions = this.getDimensions();

      style = styles[plane] = (style == 'auto') ? dimensions[plane] : style.toInt();
      size['total' + capitalized] = style;

      edges.each(function(edge){
        var edgesize = calculateEdgeSize(edge, styles);
        size['computed' + edge.capitalize()] = edgesize;
        size['total' + capitalized] += edgesize;
      });

    }, this);

    return Object.append(size, styles);
  }

});

})();


/*
---

script: Element.Position.js

name: Element.Position

description: Extends the Element native object to include methods useful positioning elements relative to others.

license: MIT-style license

authors:
  - Aaron Newton
  - Jacob Thornton

requires:
  - Core/Options
  - Core/Element.Dimensions
  - Element.Measure

provides: [Element.Position]

...
*/

(function(original){

var local = Element.Position = {

  options: {/*
    edge: false,
    returnPos: false,
    minimum: {x: 0, y: 0},
    maximum: {x: 0, y: 0},
    relFixedPosition: false,
    ignoreMargins: false,
    ignoreScroll: false,
    allowNegative: false,*/
    relativeTo: document.body,
    position: {
      x: 'center', //left, center, right
      y: 'center' //top, center, bottom
    },
    offset: {x: 0, y: 0}
  },

  getOptions: function(element, options){
    options = Object.merge({}, local.options, options);
    local.setPositionOption(options);
    local.setEdgeOption(options);
    local.setOffsetOption(element, options);
    local.setDimensionsOption(element, options);
    return options;
  },

  setPositionOption: function(options){
    options.position = local.getCoordinateFromValue(options.position);
  },

  setEdgeOption: function(options){
    var edgeOption = local.getCoordinateFromValue(options.edge);
    options.edge = edgeOption ? edgeOption :
      (options.position.x == 'center' && options.position.y == 'center') ? {x: 'center', y: 'center'} :
      {x: 'left', y: 'top'};
  },

  setOffsetOption: function(element, options){
    var parentOffset = {x: 0, y: 0},
      offsetParent = element.measure(function(){
        return document.id(this.getOffsetParent());
      }),
      parentScroll = offsetParent.getScroll();

    if (!offsetParent || offsetParent == element.getDocument().body) return;
    parentOffset = offsetParent.measure(function(){
      var position = this.getPosition();
      if (this.getStyle('position') == 'fixed'){
        var scroll = window.getScroll();
        position.x += scroll.x;
        position.y += scroll.y;
      }
      return position;
    });

    options.offset = {
      parentPositioned: offsetParent != document.id(options.relativeTo),
      x: options.offset.x - parentOffset.x + parentScroll.x,
      y: options.offset.y - parentOffset.y + parentScroll.y
    };
  },

  setDimensionsOption: function(element, options){
    options.dimensions = element.getDimensions({
      computeSize: true,
      styles: ['padding', 'border', 'margin']
    });
  },

  getPosition: function(element, options){
    var position = {};
    options = local.getOptions(element, options);
    var relativeTo = document.id(options.relativeTo) || document.body;

    local.setPositionCoordinates(options, position, relativeTo);
    if (options.edge) local.toEdge(position, options);

    var offset = options.offset;
    position.left = ((position.x >= 0 || offset.parentPositioned || options.allowNegative) ? position.x : 0).toInt();
    position.top = ((position.y >= 0 || offset.parentPositioned || options.allowNegative) ? position.y : 0).toInt();

    local.toMinMax(position, options);

    if (options.relFixedPosition || relativeTo.getStyle('position') == 'fixed') local.toRelFixedPosition(relativeTo, position);
    if (options.ignoreScroll) local.toIgnoreScroll(relativeTo, position);
    if (options.ignoreMargins) local.toIgnoreMargins(position, options);

    position.left = Math.ceil(position.left);
    position.top = Math.ceil(position.top);
    delete position.x;
    delete position.y;

    return position;
  },

  setPositionCoordinates: function(options, position, relativeTo){
    var offsetY = options.offset.y,
      offsetX = options.offset.x,
      calc = (relativeTo == document.body) ? window.getScroll() : relativeTo.getPosition(),
      top = calc.y,
      left = calc.x,
      winSize = window.getSize();

    switch(options.position.x){
      case 'left': position.x = left + offsetX; break;
      case 'right': position.x = left + offsetX + relativeTo.offsetWidth; break;
      default: position.x = left + ((relativeTo == document.body ? winSize.x : relativeTo.offsetWidth) / 2) + offsetX; break;
    }

    switch(options.position.y){
      case 'top': position.y = top + offsetY; break;
      case 'bottom': position.y = top + offsetY + relativeTo.offsetHeight; break;
      default: position.y = top + ((relativeTo == document.body ? winSize.y : relativeTo.offsetHeight) / 2) + offsetY; break;
    }
  },

  toMinMax: function(position, options){
    var xy = {left: 'x', top: 'y'}, value;
    ['minimum', 'maximum'].each(function(minmax){
      ['left', 'top'].each(function(lr){
        value = options[minmax] ? options[minmax][xy[lr]] : null;
        if (value != null && ((minmax == 'minimum') ? position[lr] < value : position[lr] > value)) position[lr] = value;
      });
    });
  },

  toRelFixedPosition: function(relativeTo, position){
    var winScroll = window.getScroll();
    position.top += winScroll.y;
    position.left += winScroll.x;
  },

  toIgnoreScroll: function(relativeTo, position){
    var relScroll = relativeTo.getScroll();
    position.top -= relScroll.y;
    position.left -= relScroll.x;
  },

  toIgnoreMargins: function(position, options){
    position.left += options.edge.x == 'right'
      ? options.dimensions['margin-right']
      : (options.edge.x != 'center'
        ? -options.dimensions['margin-left']
        : -options.dimensions['margin-left'] + ((options.dimensions['margin-right'] + options.dimensions['margin-left']) / 2));

    position.top += options.edge.y == 'bottom'
      ? options.dimensions['margin-bottom']
      : (options.edge.y != 'center'
        ? -options.dimensions['margin-top']
        : -options.dimensions['margin-top'] + ((options.dimensions['margin-bottom'] + options.dimensions['margin-top']) / 2));
  },

  toEdge: function(position, options){
    var edgeOffset = {},
      dimensions = options.dimensions,
      edge = options.edge;

    switch(edge.x){
      case 'left': edgeOffset.x = 0; break;
      case 'right': edgeOffset.x = -dimensions.x - dimensions.computedRight - dimensions.computedLeft; break;
      // center
      default: edgeOffset.x = -(Math.round(dimensions.totalWidth / 2)); break;
    }

    switch(edge.y){
      case 'top': edgeOffset.y = 0; break;
      case 'bottom': edgeOffset.y = -dimensions.y - dimensions.computedTop - dimensions.computedBottom; break;
      // center
      default: edgeOffset.y = -(Math.round(dimensions.totalHeight / 2)); break;
    }

    position.x += edgeOffset.x;
    position.y += edgeOffset.y;
  },

  getCoordinateFromValue: function(option){
    if (typeOf(option) != 'string') return option;
    option = option.toLowerCase();

    return {
      x: option.test('left') ? 'left'
        : (option.test('right') ? 'right' : 'center'),
      y: option.test(/upper|top/) ? 'top'
        : (option.test('bottom') ? 'bottom' : 'center')
    };
  }

};

Element.implement({

  position: function(options){
    if (options && (options.x != null || options.y != null)){
      return (original ? original.apply(this, arguments) : this);
    }
    var position = this.setStyle('position', 'absolute').calculatePosition(options);
    return (options && options.returnPos) ? position : this.setStyles(position);
  },

  calculatePosition: function(options){
    return local.getPosition(this, options);
  }

});

})(Element.prototype.position);


/*
---

script: Element.Shortcuts.js

name: Element.Shortcuts

description: Extends the Element native object to include some shortcut methods.

license: MIT-style license

authors:
  - Aaron Newton

requires:
  - Core/Element.Style
  - /MooTools.More

provides: [Element.Shortcuts]

...
*/

Element.implement({

  isDisplayed: function(){
    return this.getStyle('display') != 'none';
  },

  isVisible: function(){
    var w = this.offsetWidth,
      h = this.offsetHeight;
    return (w == 0 && h == 0) ? false : (w > 0 && h > 0) ? true : this.style.display != 'none';
  },

  toggle: function(){
    return this[this.isDisplayed() ? 'hide' : 'show']();
  },

  hide: function(){
    var d;
    try {
      //IE fails here if the element is not in the dom
      d = this.getStyle('display');
    } catch(e){}
    if (d == 'none') return this;
    return this.store('element:_originalDisplay', d || '').setStyle('display', 'none');
  },

  show: function(display){
    if (!display && this.isDisplayed()) return this;
    display = display || this.retrieve('element:_originalDisplay') || 'block';
    return this.setStyle('display', (display == 'none') ? 'block' : display);
  },

  swapClass: function(remove, add){
    return this.removeClass(remove).addClass(add);
  }

});

Document.implement({

  clearSelection: function(){
    if (window.getSelection){
      var selection = window.getSelection();
      if (selection && selection.removeAllRanges) selection.removeAllRanges();
    } else if (document.selection && document.selection.empty){
      try {
        //IE fails here if selected element is not in dom
        document.selection.empty();
      } catch(e){}
    }
  }

});


/*
---

script: OverText.js

name: OverText

description: Shows text over an input that disappears when the user clicks into it. The text remains hidden if the user adds a value.

license: MIT-style license

authors:
  - Aaron Newton

requires:
  - Core/Options
  - Core/Events
  - Core/Element.Event
  - Class.Binds
  - Class.Occlude
  - Element.Position
  - Element.Shortcuts

provides: [OverText]

...
*/

var OverText = new Class({

  Implements: [Options, Events, Class.Occlude],

  Binds: ['reposition', 'assert', 'focus', 'hide'],

  options: {/*
    textOverride: null,
    onFocus: function(){},
    onTextHide: function(textEl, inputEl){},
    onTextShow: function(textEl, inputEl){}, */
    element: 'label',
    labelClass: 'overTxtLabel',
    positionOptions: {
      position: 'upperLeft',
      edge: 'upperLeft',
      offset: {
        x: 4,
        y: 2
      }
    },
    poll: false,
    pollInterval: 250,
    wrap: false
  },

  property: 'OverText',

  initialize: function(element, options){
    element = this.element = document.id(element);

    if (this.occlude()) return this.occluded;
    this.setOptions(options);

    this.attach(element);
    OverText.instances.push(this);

    if (this.options.poll) this.poll();
  },

  toElement: function(){
    return this.element;
  },

  attach: function(){
    var element = this.element,
      options = this.options,
      value = options.textOverride || element.get('alt') || element.get('title');

    if (!value) return this;

    var text = this.text = new Element(options.element, {
      'class': options.labelClass,
      styles: {
        lineHeight: 'normal',
        position: 'absolute',
        cursor: 'text'
      },
      html: value,
      events: {
        click: this.hide.pass(options.element == 'label', this)
      }
    }).inject(element, 'after');

    if (options.element == 'label'){
      if (!element.get('id')) element.set('id', 'input_' + String.uniqueID());
      text.set('for', element.get('id'));
    }

    if (options.wrap){
      this.textHolder = new Element('div.overTxtWrapper', {
        styles: {
          lineHeight: 'normal',
          position: 'relative'
        }
      }).grab(text).inject(element, 'before');
    }

    return this.enable();
  },

  destroy: function(){
    this.element.eliminate(this.property); // Class.Occlude storage
    this.disable();
    if (this.text) this.text.destroy();
    if (this.textHolder) this.textHolder.destroy();
    return this;
  },

  disable: function(){
    this.element.removeEvents({
      focus: this.focus,
      blur: this.assert,
      change: this.assert
    });
    window.removeEvent('resize', this.reposition);
    this.hide(true, true);
    return this;
  },

  enable: function(){
    this.element.addEvents({
      focus: this.focus,
      blur: this.assert,
      change: this.assert
    });
    window.addEvent('resize', this.reposition);
    this.reposition();
    return this;
  },

  wrap: function(){
    if (this.options.element == 'label'){
      if (!this.element.get('id')) this.element.set('id', 'input_' + String.uniqueID());
      this.text.set('for', this.element.get('id'));
    }
  },

  startPolling: function(){
    this.pollingPaused = false;
    return this.poll();
  },

  poll: function(stop){
    //start immediately
    //pause on focus
    //resumeon blur
    if (this.poller && !stop) return this;
    if (stop){
      clearInterval(this.poller);
    } else {
      this.poller = (function(){
        if (!this.pollingPaused) this.assert(true);
      }).periodical(this.options.pollInterval, this);
    }

    return this;
  },

  stopPolling: function(){
    this.pollingPaused = true;
    return this.poll(true);
  },

  focus: function(){
    if (this.text && (!this.text.isDisplayed() || this.element.get('disabled'))) return this;
    return this.hide();
  },

  hide: function(suppressFocus, force){
    if (this.text && (this.text.isDisplayed() && (!this.element.get('disabled') || force))){
      this.text.hide();
      this.fireEvent('textHide', [this.text, this.element]);
      this.pollingPaused = true;
      if (!suppressFocus){
        try {
          this.element.fireEvent('focus');
          this.element.focus();
        } catch(e){} //IE barfs if you call focus on hidden elements
      }
    }
    return this;
  },

  show: function(){
    if (this.text && !this.text.isDisplayed()){
      this.text.show();
      this.reposition();
      this.fireEvent('textShow', [this.text, this.element]);
      this.pollingPaused = false;
    }
    return this;
  },

  test: function(){
    return !this.element.get('value');
  },

  assert: function(suppressFocus){
    return this[this.test() ? 'show' : 'hide'](suppressFocus);
  },

  reposition: function(){
    this.assert(true);
    if (!this.element.isVisible()) return this.stopPolling().hide();
    if (this.text && this.test()){
      this.text.position(Object.merge(this.options.positionOptions, {
        relativeTo: this.element
      }));
    }
    return this;
  }

});

OverText.instances = [];

Object.append(OverText, {

  each: function(fn){
    return OverText.instances.each(function(ot, i){
      if (ot.element && ot.text) fn.call(OverText, ot, i);
    });
  },

  update: function(){

    return OverText.each(function(ot){
      return ot.reposition();
    });

  },

  hideAll: function(){

    return OverText.each(function(ot){
      return ot.hide(true, true);
    });

  },

  showAll: function(){
    return OverText.each(function(ot){
      return ot.show();
    });
  }

});



/*
---

name: Locale.es-ES.Date

description: Date messages for Spanish.

license: MIT-style license

authors:
  - Ãlfons Sanchez

requires:
  - /Locale

provides: [Locale.es-ES.Date]

...
*/

Locale.define('es-ES', 'Date', {

  months: ['Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio', 'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre'],
  months_abbr: ['ene', 'feb', 'mar', 'abr', 'may', 'jun', 'jul', 'ago', 'sep', 'oct', 'nov', 'dic'],
  days: ['Domingo', 'Lunes', 'Martes', 'Miércoles', 'Jueves', 'Viernes', 'Sábado'],
  days_abbr: ['dom', 'lun', 'mar', 'mié', 'juv', 'vie', 'sáb'],

  // Culture's date order: DD/MM/YYYY
  dateOrder: ['date', 'month', 'year'],
  shortDate: '%d/%m/%Y',
  shortTime: '%H:%M',
  AM: 'AM',
  PM: 'PM',
  firstDayOfWeek: 1,

  // Date.Extras
  ordinal: '',

  lessThanMinuteAgo: 'hace menos de un minuto',
  minuteAgo: 'hace un minuto',
  minutesAgo: 'hace {delta} minutos',
  hourAgo: 'hace una hora',
  hoursAgo: 'hace unas {delta} horas',
  dayAgo: 'hace un día',
  daysAgo: 'hace {delta} días',
  weekAgo: 'hace una semana',
  weeksAgo: 'hace unas {delta} semanas',
  monthAgo: 'hace un mes',
  monthsAgo: 'hace {delta} meses',
  yearAgo: 'hace un año',
  yearsAgo: 'hace {delta} años',

  lessThanMinuteUntil: 'menos de un minuto desde ahora',
  minuteUntil: 'un minuto desde ahora',
  minutesUntil: '{delta} minutos desde ahora',
  hourUntil: 'una hora desde ahora',
  hoursUntil: 'unas {delta} horas desde ahora',
  dayUntil: 'un día desde ahora',
  daysUntil: '{delta} días desde ahora',
  weekUntil: 'una semana desde ahora',
  weeksUntil: 'unas {delta} semanas desde ahora',
  monthUntil: 'un mes desde ahora',
  monthsUntil: '{delta} meses desde ahora',
  yearUntil: 'un año desde ahora',
  yearsUntil: '{delta} años desde ahora'

});


/*
---

name: Locale.fr-FR.Date

description: Date messages for French.

license: MIT-style license

authors:
  - Nicolas Sorosac
  - Antoine Abt

requires:
  - /Locale

provides: [Locale.fr-FR.Date]

...
*/

Locale.define('fr-FR', 'Date', {

  months: ['Janvier', 'Février', 'Mars', 'Avril', 'Mai', 'Juin', 'Juillet', 'Août', 'Septembre', 'Octobre', 'Novembre', 'Décembre'],
  months_abbr: ['janv.', 'févr.', 'mars', 'avr.', 'mai', 'juin', 'juil.', 'août', 'sept.', 'oct.', 'nov.', 'déc.'],
  days: ['Dimanche', 'Lundi', 'Mardi', 'Mercredi', 'Jeudi', 'Vendredi', 'Samedi'],
  days_abbr: ['dim.', 'lun.', 'mar.', 'mer.', 'jeu.', 'ven.', 'sam.'],

  // Culture's date order: DD/MM/YYYY
  dateOrder: ['date', 'month', 'year'],
  shortDate: '%d/%m/%Y',
  shortTime: '%H:%M',
  AM: 'AM',
  PM: 'PM',
  firstDayOfWeek: 1,

  // Date.Extras
  ordinal: function(dayOfMonth){
    return (dayOfMonth > 1) ? '' : 'er';
  },

  lessThanMinuteAgo: "il y a moins d'une minute",
  minuteAgo: 'il y a une minute',
  minutesAgo: 'il y a {delta} minutes',
  hourAgo: 'il y a une heure',
  hoursAgo: 'il y a {delta} heures',
  dayAgo: 'il y a un jour',
  daysAgo: 'il y a {delta} jours',
  weekAgo: 'il y a une semaine',
  weeksAgo: 'il y a {delta} semaines',
  monthAgo: 'il y a 1 mois',
  monthsAgo: 'il y a {delta} mois',
  yearthAgo: 'il y a 1 an',
  yearsAgo: 'il y a {delta} ans',

  lessThanMinuteUntil: "dans moins d'une minute",
  minuteUntil: 'dans une minute',
  minutesUntil: 'dans {delta} minutes',
  hourUntil: 'dans une heure',
  hoursUntil: 'dans {delta} heures',
  dayUntil: 'dans un jour',
  daysUntil: 'dans {delta} jours',
  weekUntil: 'dans 1 semaine',
  weeksUntil: 'dans {delta} semaines',
  monthUntil: 'dans 1 mois',
  monthsUntil: 'dans {delta} mois',
  yearUntil: 'dans 1 an',
  yearsUntil: 'dans {delta} ans'

});


/*
---

name: Locale.he-IL.Date

description: Date messages for Hebrew.

license: MIT-style license

authors:
  - Elad Ossadon

requires:
  - /Locale

provides: [Locale.he-IL.Date]

...
*/

Locale.define('he-IL', 'Date', {

  months: ['ינואר', 'פברואר', 'מרץ', 'אפריל', 'מאי', 'יוני', 'יולי', 'אוגוסט', 'ספטמבר', 'אוקטובר', 'נובמבר', 'דצמבר'],
  months_abbr: ['ינואר', 'פברואר', 'מרץ', 'אפריל', 'מאי', 'יוני', 'יולי', 'אוגוסט', 'ספטמבר', 'אוקטובר', 'נובמבר', 'דצמבר'],
  days: ['ראשון', 'שני', 'שלישי', 'רביעי', 'חמישי', 'שישי', 'שבת'],
  days_abbr: ['ראשון', 'שני', 'שלישי', 'רביעי', 'חמישי', 'שישי', 'שבת'],

  // Culture's date order: MM/DD/YYYY
  dateOrder: ['date', 'month', 'year'],
  shortDate: '%d/%m/%Y',
  shortTime: '%H:%M',
  AM: 'AM',
  PM: 'PM',
  firstDayOfWeek: 0,

  // Date.Extras
  ordinal: '',

  lessThanMinuteAgo: 'לפני פחות מדקה',
  minuteAgo: 'לפני כדקה',
  minutesAgo: 'לפני {delta} דקות',
  hourAgo: 'לפני כשעה',
  hoursAgo: 'לפני {delta} שעות',
  dayAgo: 'לפני יום',
  daysAgo: 'לפני {delta} ימים',
  weekAgo: 'לפני שבוע',
  weeksAgo: 'לפני {delta} שבועות',
  monthAgo: 'לפני חודש',
  monthsAgo: 'לפני {delta} חודשים',
  yearAgo: 'לפני שנה',
  yearsAgo: 'לפני {delta} שנים',

  lessThanMinuteUntil: 'בעוד פחות מדקה',
  minuteUntil: 'בעוד כדקה',
  minutesUntil: 'בעוד {delta} דקות',
  hourUntil: 'בעוד כשעה',
  hoursUntil: 'בעוד {delta} שעות',
  dayUntil: 'בעוד יום',
  daysUntil: 'בעוד {delta} ימים',
  weekUntil: 'בעוד שבוע',
  weeksUntil: 'בעוד {delta} שבועות',
  monthUntil: 'בעוד חודש',
  monthsUntil: 'בעוד {delta} חודשים',
  yearUntil: 'בעוד שנה',
  yearsUntil: 'בעוד {delta} שנים'

});


/*
---

name: Locale.zh-CH.Date

description: Date messages for Chinese (simplified and traditional).

license: MIT-style license

authors:
  - YMind Chan

requires:
  - /Locale

provides: [Locale.zh-CH.Date]

...
*/

// Simplified Chinese
Locale.define('zh-CHS', 'Date', {

  months: ['一月', '二月', '三月', '四月', '五月', '六月', '七月', '八月', '九月', '十月', '十一月', '十二月'],
  months_abbr: ['一', '二', '三', '四', '五', '六', '七', '八', '九', '十', '十一', '十二'],
  days: ['星期日', '星期一', '星期二', '星期三', '星期四', '星期五', '星期六'],
  days_abbr: ['日', '一', '二', '三', '四', '五', '六'],

  // Culture's date order: YYYY-MM-DD
  dateOrder: ['year', 'month', 'date'],
  shortDate: '%Y-%m-%d',
  shortTime: '%I:%M%p',
  AM: 'AM',
  PM: 'PM',
  firstDayOfWeek: 1,

  // Date.Extras
  ordinal: '',

  lessThanMinuteAgo: '不到1分钟前',
  minuteAgo: '大约1分钟前',
  minutesAgo: '{delta}分钟之前',
  hourAgo: '大约1小时前',
  hoursAgo: '大约{delta}小时前',
  dayAgo: '1天前',
  daysAgo: '{delta}天前',
  weekAgo: '1星期前',
  weeksAgo: '{delta}星期前',
  monthAgo: '1个月前',
  monthsAgo: '{delta}个月前',
  yearAgo: '1年前',
  yearsAgo: '{delta}年前',

  lessThanMinuteUntil: '从现在开始不到1分钟',
  minuteUntil: '从现在开始約1分钟',
  minutesUntil: '从现在开始约{delta}分钟',
  hourUntil: '从现在开始1小时',
  hoursUntil: '从现在开始约{delta}小时',
  dayUntil: '从现在开始1天',
  daysUntil: '从现在开始{delta}天',
  weekUntil: '从现在开始1星期',
  weeksUntil: '从现在开始{delta}星期',
  monthUntil: '从现在开始一个月',
  monthsUntil: '从现在开始{delta}个月',
  yearUntil: '从现在开始1年',
  yearsUntil: '从现在开始{delta}年'

});

// Traditional Chinese
Locale.define('zh-CHT', 'Date', {

  months: ['一月', '二月', '三月', '四月', '五月', '六月', '七月', '八月', '九月', '十月', '十一月', '十二月'],
  months_abbr: ['一', '二', '三', '四', '五', '六', '七', '八', '九', '十', '十一', '十二'],
  days: ['星期日', '星期一', '星期二', '星期三', '星期四', '星期五', '星期六'],
  days_abbr: ['日', '一', '二', '三', '四', '五', '六'],

  // Culture's date order: YYYY-MM-DD
  dateOrder: ['year', 'month', 'date'],
  shortDate: '%Y-%m-%d',
  shortTime: '%I:%M%p',
  AM: 'AM',
  PM: 'PM',
  firstDayOfWeek: 1,

  // Date.Extras
  ordinal: '',

  lessThanMinuteAgo: '不到1分鐘前',
  minuteAgo: '大約1分鐘前',
  minutesAgo: '{delta}分鐘之前',
  hourAgo: '大約1小時前',
  hoursAgo: '大約{delta}小時前',
  dayAgo: '1天前',
  daysAgo: '{delta}天前',
  weekAgo: '1星期前',
  weeksAgo: '{delta}星期前',
  monthAgo: '1个月前',
  monthsAgo: '{delta}个月前',
  yearAgo: '1年前',
  yearsAgo: '{delta}年前',

  lessThanMinuteUntil: '從現在開始不到1分鐘',
  minuteUntil: '從現在開始約1分鐘',
  minutesUntil: '從現在開始約{delta}分鐘',
  hourUntil: '從現在開始1小時',
  hoursUntil: '從現在開始約{delta}小時',
  dayUntil: '從現在開始1天',
  daysUntil: '從現在開始{delta}天',
  weekUntil: '從現在開始1星期',
  weeksUntil: '從現在開始{delta}星期',
  monthUntil: '從現在開始一個月',
  monthsUntil: '從現在開始{delta}個月',
  yearUntil: '從現在開始1年',
  yearsUntil: '從現在開始{delta}年'

});
