# AI translation of Bloom's UI strings

Where we can trust AI translation of Bloom's Crowdin strings, whether to use one model or
several, and a suggested process for adding and auditing AI translations on Crowdin.

## Part 1: The data

Full report, with every tested string and each model's translation:
https://claude.ai/artifact/UWX6JUKB37YykJ9dSfDNsT (a Claude artifact, shared with anyone who has
the link). The French and Spanish regional results are only in this doc.

### What we did (2026-10-28)

- **Blind test.** For each of the 26 languages with at least 10 approved, current translations
  in master's `Bloom.xlf`, `BloomMediumPriority.xlf` and `BloomLowPriority.xlf`, three models
  (Opus 5.5, Sonnet 5.5, Fable 5.1) each translated the same 10 English strings without seeing
  the approved text. Each ran as its own agent at its default thinking level, without
  consulting the code, so that the test stayed blind. Opus and Sonnet then each did a second,
  independent run, to compare a same-model check with a cross-model one. Not tested:
  Vietnamese, Kyrgyz, Tajik and Uzbek had fewer than 10 approved strings, and Tagalog, Cornish,
  Pulaar, Sgaw Karen and Yucatec Maya had none. The sampling favoured strings approved in many
  languages, so most languages got the same strings: the 260 translations per run cover only 34
  different English strings, mostly short labels of 3 to 6 words ("Advanced Program Settings"
  is in 26 languages). The results describe short UI labels, and the 260 are far from
  independent.
- **Portuguese regional test.** Each model translated 100 approved Portuguese strings three
  times: for Brazil, for Lusophone Africa and for Portugal.
- **Score.** chrF, a character-level similarity from 0 to 100, between translations. A
  different but correct translation typically scores 40 to 70, so a score measures distance from
  the approved wording, not quality on its own. "Agreement" is the same score between two
  models' translations, averaged both ways round. Where a table says "vs approved" for a check,
  it scores Opus's first-run translation.
- **Times** are wall-clock minutes:seconds per agent; the agents ran in parallel.

### Results

**Agreement between models predicts accuracy.** Across all 260 tested strings:

| How closely the three models agree | Strings | Average similarity to approved |
| --- | --- | --- |
| 80 or more | 113 | 64 |
| 60 to 79 | 79 | 49 |
| under 60 | 68 | 28 |

This is not just easy languages scoring high on both: it holds within languages too (correlation
0.42 within languages against 0.46 overall, and positive in 22 of 26 languages). Both columns
use the same character score, so short strings and scripts without spaces pull both down a
little. Still, agreement is a confidence signal we can compute for any new string, with no
reference translation needed.

**A second model doesn't make the translation better.** Picking, per string, the translation
the other two agree with most scores 50.9, against 50.6 for the best single model. Picking the
right model per string would reach 56.4, but nothing tells us in advance which one that is. That
makes a design choice natural: one model produces the translation and a second decides whether
it gets added.

**The check should be a different model, not the same one twice.** Opus and Sonnet each
translated the 260 strings a second time, blind. Treating each pairing as a check that adds a
translation only when the two agree at 60 or more:

| Check | Average agreement | Strings passed | Passed: vs approved | Stopped: vs approved |
| --- | --- | --- | --- | --- |
| Opus twice | 87 | 221 | 53 | 36 |
| Sonnet twice | 80 | 203 | 54 | 37 |
| Opus and Sonnet | 77 | 189 | 57 | 34 |
| Opus and Fable | 75 | 181 | 59 | 32 |

Two runs of one model agree even when wrong: two Opus runs gave identical text for every
Azerbaijani and Dari string, though Azerbaijani is among the furthest from the approved
wording. 44 strings pass two Opus runs but fail Opus with Sonnet, and Opus's translations of
those average 37 against the approved text; only 12 go the other way, averaging a fine 54. A
same-model check does still catch outright guessing (two Opus runs agree at only 40 on Mam),
but on K'iche' two Opus runs agree at 70 while Opus and Sonnet agree at 51. A stricter check
always passes fewer strings, so we also compared the two at equal pass counts: keeping the 180
most-agreed strings, two Opus runs pass strings averaging 54.5 and Opus with Sonnet 57.3, and the gap widens as the bar rises.

**Which model did best: no meaningful difference.** The three are within a point of each other;
Opus was closest in the most languages and Fable on slightly the most strings (164 against
161), but on 10 strings per language neither is decisive.

| Model | Average vs approved | Languages closest | Agreement with the other two | Time: 26 languages / Portuguese |
| --- | --- | --- | --- | --- |
| Opus 5.5 | 50.6 | 12 of 26 | 75.9 | 2:51 / 1:51 |
| Fable 5.1 | 50.6 | 9 of 26 | 73.4 | 4:21 / 4:56 |
| Sonnet 5.5 | 49.7 | 5 of 26 | 74.2 | 2:33 / 1:41 |

**Languages, by what the models actually did** (closest model's similarity to approved ·
how much the three models agree). The cut-offs: Confident needs 60 or more against approved and
78 or more agreement; Usable, 50 or more against approved; Unclear, under 50 against approved
but 70 or more agreement; everything else is No confidence.

- **Confident:** Russian (75 · 78), Portuguese (73 · 85), French (72 · 87), Turkish (70 · 86),
  Indonesian (68 · 86), Arabic (67 · 82), Khmer (65 · 80), Spanish (62 · 86).
- **Usable with review:** Nepali (61 · 67), Swahili (59 · 80), Bengali (57 · 69),
  Kinyarwanda (55 · 63), Thai (54 · 84), Hindi (53 · 78), Tamil (51 · 80).
- **Unclear, check the approvals first:** Dari (47 · 77), Hausa (45 · 76), Burmese (40 · 83),
  Chinese (39 · 79), Pashto (37 · 73), Azerbaijani (34 · 70), Lao (31 · 82). The models agree
  with each other but not with the approved text. 50 strings across all languages fit that
  pattern; the report lists them. Some approvals there look weak (Lao reads like literal machine
  translation); some are better than the models (Chinese 封三 is the printers' term for an
  inside back cover). Chinese may belong higher: see the Chinese check below, which a larger
  re-test should confirm.
- **No confidence:** Amharic (48 · 68), Telugu (46 · 64), K'iche' (36 · 49), Mam (28 · 22).
  In Mam the models don't even agree with each other: they are guessing.

**Approved Portuguese is Brazilian.** Averaged over the three models, the Brazilian version
came closest to the approved text on 54 of 100 strings, Portugal's on 9 and Africa's on 4; 33
were ties (within half a point). On average the Brazilian versions scored about 73 against 66
for the others. Across all 2,034 approved Portuguese translations, *arquivo* appears 92 times
and *ficheiro* never, *tela* 23 times and *ecrã* 4, and *estar* plus a gerund (Brazilian) 21
times against *estar a* plus an infinitive (European) once. The
models' African and European versions came out nearly the same, so the test can't separate
those two. Crowdin's code for the language is pt-PT anyway; the AI drafts follow what
translators actually approved, so users in Portugal get Brazilian Portuguese, as they already do.

**Approved Spanish is Latin American; approved French is standard French.** Opus and Sonnet each
translated 50 approved strings per language once for each region. For each string, the region
whose versions (averaged over both models) came closest to the approved text wins; scores within
half a point are a tie:

| Language | Closest to the approved text, of 50 strings |
| --- | --- |
| Spanish | Latin America 30, Spain 13, tie 7 |
| French | France 19, Africa 11, Canada 9, tie 11 |

Both models put Latin American Spanish closer than Spain's, and the approved Spanish uses
*computadora* 31 times and *ordenador* never, with no *vosotros* forms. Not every sign agrees:
the Spanish spelling *vídeo* (28) outnumbers *video* (10). The French leans to the standard
written French of France rather than Quebec's, but less clearly. As with Portuguese, written
French in Africa follows the same standard, so the test can't separate those two (Sonnet's
France and Africa versions were identical on 40 of 50 strings).

**Placeholders.** 18 of the 260 test strings have a `{0}`-style placeholder, though 12 of them
are the same "Default Font for {0}". Across all five runs, none of those 90 translations lost
or changed one. That is encouraging, not proof.

**Chinese, checked by meaning.** Reading the 10 Chinese strings rather than scoring them by
characters:

- 6 are fine: one exact match, four equivalent wordings (编辑此书 against 编辑本书 scores 27
  for one changed character; for "Basic Text & Image", Fable's 基本文字和图片 keeps the "Basic"
  the approved text drops), and one where the models are better (图片居中 against 图像在中间
  scores 10).
- 2 are terminology: Bloom's translators use 丛书 for "collection" and 封三 for "inside back
  cover"; the models chose other valid words. A glossary fixes these.
- 1 is a domain error by all three models: "Decodable Reader" as 可解码 (decodable in the data
  sense) instead of 拼读 (phonics).
- 1 is an error by Sonnet only: 阅读器 (an e-reader device) for "reader" (a reading book).

So most of Chinese's low score is the scoring, not the translations.

### Limits

Ten strings per language is a small sample, and the same 34 short strings recur across
languages, so longer sentences and the less common strings are untested. Approved does not
always mean correct. The scores
reward matching the approved wording, which penalizes good alternatives.

The score compares characters, so it can't tell a synonym from a mistake. That hurts most where
one character is a whole word (Chinese) and in other scripts without spaces (Lao, Thai,
Burmese, Khmer): one changed word drops a short string's score sharply. Their tiers above are
probably too low.

## Part 2: A suggested process

This part is AI-generated. A developer guided it, but nobody has inspected it carefully. Hold it
loosely: it is a suggestion to react to and change, not a decision.

Two skills, separate from `merge-crowdin-translations`, covering every Crowdin source file:
all of `DistFiles/localization/en/*.xlf` and the template `ReadMe-en.xlf` documents. The test
covered only UI strings, so the ReadMes (long prose, translated paragraph by paragraph) are the
least proven part.

- **Add translations.** Fills untranslated strings with AI drafts. Run once to backfill. After
  that, the suggestion is to run it on demand right after each `merge-crowdin-translations` run:
  the developer is already doing Crowdin work then, and anything released since the last batch
  is on Crowdin by that time. Most of what follows applies to this skill.
- **Audit translations.** Reviews existing translations and brings what it finds to the
  developer. Run once at first; it can run again as translations come in. See "The audit skill"
  below.

### The two bars

- **Approving wording: AI never does it.** Approval is what Bloom shows users by default and
  what a proofreader vouches for. No result above justifies an AI approval, and none is likely
  to: even in the best languages the closest model scores only about 70 against the approved
  wording. The one exception stays as it is in `merge-crowdin-translations`: a fix that changes
  only placeholders or markup, agreed by a developer, may be approved by the bot.
- **Adding an unapproved translation: a low bar.** Bloom hides unapproved translations unless the
  user turns on "show unapproved translations", so an AI suggestion doesn't reach ordinary users.
  On Crowdin it gives the translator a draft to approve, edit or replace.

  One exception needs a build change before AI drafts go into those files: documents baked into
  HTML at build time (the template ReadMes, via `l10n-build.js` and XliffForHtml's `--inject`).
  The injector uses every translation in the file, approved or not, so an AI draft there would
  reach every user. The build should leave out AI-drafted units that are still unapproved
  (the ledger identifies the drafts) when it injects; once a translator approves one, it goes
  in like any other.

Unapproved is enough of a flag for users. For translators, every AI translation goes in as
SILCrowdinBot, which is how they can tell it came from AI, and each language's translators get
one notification per batch (starting `[AI-generated message to translators]`) saying the bot's
translations are machine drafts awaiting review.

### Which languages need it

AI drafts help most where nobody is translating. Where people actively are, they are at best
redundant and at worst get in the way: a draft sitting on a string can discourage a translator
from writing their own. So before anything else, sort the languages by two facts from Crowdin:

- **Coverage:** the share of strings in `Bloom.xlf` and `BloomMediumPriority.xlf` that have a
  human translation. Crowdin's own progress figure won't do: it counts the bot's drafts, so after
  one backfill every drafted language would look complete. Count from the API, leaving out
  SILCrowdinBot's translations.
- **Activity:** whether people are translating now: human translations or approvals in the last
  six months, from Crowdin's top-members report. That report also counts the bot, the project
  owner and managers (including a developer's own placeholder approvals), so leave all of them
  out, using a list of those accounts kept in the skill.

Suggested starting points, to adjust with experience: "nearly complete" is 90% or more;
"active" is people outside that list translating or approving at least 20 strings in total in
the six months. Then:

- **Active and nearly complete** (Portuguese, for example): don't add drafts. New strings get
  translated by people soon enough. The audit can still be useful there.
- **Active but behind:** give people a head start: draft a string only once it has been on
  Crowdin for a month (from the string's creation date on Crowdin). Better still, ask that
  language's translators whether they want drafts at all.
- **Inactive, with gaps:** this is where the add skill pays off. Drafts are the only way those
  strings move at all, and "show unapproved translations" users get them right away. In an
  inactive language, an audit issue reaches nobody, so the audit should suggest fixing or
  skipping there rather than raising an issue.

This sort only narrows things further: the tier rules in "When to add a translation" still
apply, so an inactive No-confidence language still gets no drafts. Record per language what
its translators said about drafts, when they were asked, and any manual override of the sort,
in a small settings file beside the ledger. Revisit the sort on each run, since a language can
become active again.

### When to add a translation

Add an AI translation for a string in a language only when all of these hold:

1. **The string has no translation in that language at all, on Crowdin.** Check through the
   Crowdin API, not the repo: the repo's files trail Crowdin until the next merge and show only
   one translation per string. Never compete with or replace a human's translation, approved or
   not. Add each translation through the API, one string at a time, as the merge skill already
   does.
2. **The English is final:** the entry has no `translate="no"`.
3. **The language is in the Confident or Usable tier.** Unclear-tier languages wait until a
   fluent reviewer has looked at the flagged strings. No-confidence languages get nothing.
   Untested languages (listed in Part 1) need a test first: 10 approved strings to test
   against, or a fluent translator spot-checking about 20 drafts. Inactive languages may never
   get 10 approvals, so the spot check is the way in for any untested language whose
   translator agrees to it; Vietnamese and Tagalog, widely used languages the models handle
   well, are the first candidates.
4. **Opus and Sonnet agree** on that string: their two translations score 60 or more against
   each other. Below 60, ask Fable, as a judge, whether the two mean the same thing; character
   scores misjudge synonyms, especially in Chinese and other scripts without spaces. If the judge
   says they differ in meaning, skip the string; it stays untranslated for a person. In the
   test, everything the 60 bar stopped averaged 34 against the approved text, against 57 for what
   it passed. The judge only rescues synonyms from that stopped group, so what it still rejects
   should be further off still, though that wasn't measured.
5. **The language's sort allows it** (see "Which languages need it"): not active and nearly
   complete, and for an active language behind on its work, the string has been on Crowdin for
   a month.

### How many agents

**Two, running different models.** Opus translates; Sonnet translates the same string
independently as the check, without seeing Opus's answer. Opus's translation is the one added.
Running Opus twice is not a substitute: the runs agree too readily, and let through strings a
second model would stop. A third model as another translator wouldn't make the translation
better; Fable's only role is the judge in rule 4, consulted when the two seem to disagree.
Sonnet is the checker because it is fast. Compared at equal pass counts, Fable checks about as
well (1.5 points better at the loosest bar, level at stricter ones) and takes 1.5 to 3 times as
long.

### What the models are given

- **The code.** Both agents should consult the code as needed: where the string appears, whether
  it is a button, a title or a sentence, and what each placeholder stands for. This context is a
  main benefit of translating locally rather than on Crowdin. Each agent works independently and
  never sees the other's translation.
- The English, the string's translator notes, and the id (which often says where it appears).
- That language's approved translations of Bloom's core terms (book, collection, template,
  publish, page, and so on), so AI drafts use the words translators already chose. Include the
  literacy-teaching terms (decodable reader, leveled reader, sight words, stages, levels): the
  Chinese check shows models get these wrong without help.
- The target variety, matching the approved text: Brazilian Portuguese, Latin American Spanish,
  and standard written French (avoiding colloquialisms that read oddly outside France).

### Feedback

Record each batch: per language, how many strings were added and how many skipped for
disagreement. Before the next batch, count what translators did with the previous one: approved
as is, edited, or replaced. That tells us whether the 60 bar is right, per language, and moves
languages between tiers with real evidence.

### Measuring by meaning

Future measurements, including the feedback above and any re-test of the tiers, should judge
meaning rather than characters: a judge model (or a fluent reader) decides whether each AI
translation is an acceptable alternative to the approved one, with the same meaning, natural
wording and Bloom's usual terms. Re-test Chinese, Lao, Thai, Burmese and Khmer that way first,
on a larger sample (about 50 strings each), with and without the glossary.

### The audit skill

The audit acts on nothing by itself. It brings its findings to the developer on a decider form,
grouped by kind and by confidence, each group with a suggested action: fix it on Crowdin as
SILCrowdinBot, raise a Crowdin issue for the translators (starting
`[AI-generated message to translators]`), or skip it. The developer chooses per group, and can
pick out single findings. For an approved translation, fixing on Crowdin is only an option for a
placeholder-only fix, which the bot may approve: any other bot fix would sit unseen under the
approved text, so the choice there is an issue or skip. What it looks for, most certain first:

1. **Mechanical problems:** a placeholder, markup or escape that doesn't match the English.
   These need no knowledge of the language. A fix chosen by the developer follows the merge
   skill's rules, including approving placeholder-only fixes.
2. **Meaning errors:** two different models translate the string blind, as in the add skill.
   A finding requires that the two agree with each other in meaning and that a judge says the
   existing translation means something different. First check the existing translation
   against the language's glossary: if it uses the glossary's term, it is not an error, however
   the models phrase it (Chinese 封三 is better than the models' wording). Include the models'
   wording as a suggestion. The 50 "agree but differ" strings in the study are this case.
3. **Terminology:** the translation uses a different word for a core term than the language's
   glossary.

Limits:

- Confident and Usable languages get meaning and terminology findings. Unclear languages get
  them only when Opus, Sonnet and Fable all agree, since that tier is where the approvals
  themselves are in doubt (Lao). No-confidence languages get none: there the models aren't
  reliable enough to second-guess a person. Mechanical problems are found in every language.
- How many issues to send each language in one run is the developer's call on the form, so
  translators aren't flooded; the most certain go first.
- Approved translations are audited too. An issue on one goes to the language's proofreaders.

### Tracking audits, and what Bloom shows

**A ledger of what has been audited.** One file per language in a folder at the repo root, such
as `localization-audit/<lang>.json`: outside `DistFiles`, so it doesn't ship with Bloom, and
outside the folders Crowdin syncs. Each entry holds the string's id, a hash of its English, a
hash of the translation judged, the translation's author, the verdict, the models, and the
date. Both hashes are of normalized text (XML escapes decoded, whitespace collapsed), the same
normalization on every side, so the text Crowdin stores and the text in the exported file hash
alike.

The verdicts: **drafted** (the add skill wrote it), **confirmed**, **fixed on Crowdin**, **issue
raised**, **developer skipped**, and **tier skipped** (the language's tier rules it out). A
string whose two hashes still match is not audited again, so a finding the developer skipped
stays skipped. The one exception is **tier skipped**: those strings are audited again whenever
their language's tier or sort changes. If a translator edits the translation or the English
changes, the hashes stop matching and the string is audited again, with nobody having to
remember.

The ledger is keyed by hashes, so it is safe on any branch: an entry only applies where the
branch's English and translation match what was judged. Release branches need it too, and
today nothing carries it there: `merge-crowdin-translations` would have to change to commit
master's current ledger to each release branch, as its own commit beside the translations
(its translation commit is checked to hold only translation files).

**Confirmed translations.** A human-written translation that isn't approved yet, but that two
different models, translating blind, independently agree with in meaning. That is real evidence:
a person and two models reached the same meaning. Authorship comes from the Crowdin API, not the
repo, which doesn't record it. Anything by SILCrowdinBot, and any machine pre-translation, counts
as AI: an AI draft can never be confirmed, because AI checking AI doesn't meet the bar for
showing it to users. Since a confirmation changes what users see, the audit puts each batch of
them on the developer's decider form like any other finding; only the ones the developer
accepts are recorded as confirmed.

**Decide at build time what Bloom shows.** Translations are fixed once Bloom is built, so the
build can decide everything Bloom now works out at runtime:

- In the copy of the translation files Bloom ships, and never in the repo, mark confirmed
  translations `approved="yes"`, but only where the shipped text still matches the hash the
  ledger judged. L10NSharp decides what is approved from that attribute alone, so Bloom then
  shows them by default with no change to L10NSharp. The repo and Crowdin still record exactly
  what a person approved.
- Compute the UI-language lists, instead of Bloom counting at runtime: one list for the default
  menu (approved plus confirmed strings) and one for when "show unapproved translations" is on
  (all translated strings, AI drafts included: users who turn it on have opted in to unreviewed
  text). A suggested rule: keep today's bars, 25% on release channels and 1% on alpha, but count
  only `Bloom.xlf` and `BloomMediumPriority.xlf`, the strings people see most, rather than every
  file. A short hand-kept list in the repo can leave out a language the developer judges too
  weak despite its count. Bloom reads the lists. Each branch's build computes its own, since
  each branch's English differs.
- Print the lists in the build log, so a change in which languages Bloom offers is visible.
