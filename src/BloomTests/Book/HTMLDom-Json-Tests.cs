using Bloom.Book;
using NUnit.Framework;

namespace BloomTests.Book
{
    [TestFixture]
    public sealed class HtmlDom_Json_Tests
    {
        [Test]
        public void GetBookJson_EmptyDom_ReturnsEmptyArray()
        {
            var dom = new HtmlDom("<html><body></body></html>");
            Assert.AreEqual("[]", dom.GetTextsJson());
        }

        [Test]
        public void GetBookJson_SingleTranslationGroup_ReturnsExpectedJson()
        {
            var dom = new HtmlDom(
                @"<html><body>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>Hello</div>
                        <div class='bloom-editable' lang='es'>Hola</div>
                    </div>
                </body></html>"
            );

            var json = dom.GetTextsJson();
            Assert.That(json, Is.EqualTo(@"[{""en"":""Hello"",""es"":""Hola""}]"));
        }

        [Test]
        public void GetBookJson_MultipleTranslationGroups_ReturnsExpectedJson()
        {
            var dom = new HtmlDom(
                @"<html><body>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>First</div>
                        <div class='bloom-editable' lang='es'>Primero</div>
                    </div>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>Second</div>
                        <div class='bloom-editable' lang='es'>Segundo</div>
                    </div>
                </body></html>"
            );

            var json = dom.GetTextsJson();
            Assert.That(
                json,
                Is.EqualTo(
                    @"[{""en"":""First"",""es"":""Primero""},{""en"":""Second"",""es"":""Segundo""}]"
                )
            );
        }

        [Test]
        public void GetBookJson_EditableWithoutLang_Ignored()
        {
            var dom = new HtmlDom(
                @"<html><body>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable'>No lang</div>
                        <div class='bloom-editable' lang='en'>Has lang</div>
                    </div>
                </body></html>"
            );

            var json = dom.GetTextsJson();
            Assert.That(json, Is.EqualTo(@"[{""en"":""Has lang""}]"));
        }

        [Test]
        public void GetBookJson_GroupWithoutEditables_NotIncluded()
        {
            var dom = new HtmlDom(
                @"<html><body>
                    <div class='bloom-translationGroup'>
                        <div>Not an editable</div>
                    </div>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>Valid group</div>
                    </div>
                </body></html>"
            );

            var json = dom.GetTextsJson();
            Assert.That(json, Is.EqualTo(@"[{""en"":""Valid group""}]"));
        }

        [Test]
        public void GetBookJson_EmptyEditables_ExcludedAfterTrimming()
        {
            var dom = new HtmlDom(
                @"<html><body>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>Not empty</div>
                        <div class='bloom-editable' lang='es'>  </div>
                        <div class='bloom-editable' lang='fr'>
                        </div>
                        <div class='bloom-editable' lang='de'></div>
                    </div>
                </body></html>"
            );

            var json = dom.GetTextsJson();
            Assert.That(json, Is.EqualTo(@"[{""en"":""Not empty""}]"));
        }
    }
}
