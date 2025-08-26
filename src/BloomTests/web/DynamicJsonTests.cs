using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Api;
using NUnit.Framework;

namespace BloomTests.web
{
    enum TestVal
    {
        First,
        Second,
        Third,
    }

    /// <summary>
    /// A very incomplete set, just tries some new functionality.
    /// </summary>
    public class DynamicJsonTests
    {
        [Test]
        public void CanReadStringNumberDictAsStringEnumDict()
        {
            var obj =
                DynamicJson.Parse("{'d': {'a1':2, 'a2': 0, 'a3': 1}}".Replace("'", "\""))
                as DynamicJson;
            Assert.That(
                obj.TryGet<Dictionary<string, TestVal>>("d", out Dictionary<string, TestVal> val),
                Is.True
            );
            Assert.That(val["a1"], Is.EqualTo(TestVal.Third));
            Assert.That(val["a2"], Is.EqualTo(TestVal.First));
            Assert.That(val["a3"], Is.EqualTo(TestVal.Second));
        }

        [Test]
        public void CanReadStringNameDictAsStringEnumDict()
        {
            var obj =
                DynamicJson.Parse(
                    "{'d': {'a1':'Third', 'a2': 'First', 'a3': 'Second'}}".Replace("'", "\"")
                ) as DynamicJson;
            Assert.That(
                obj.TryGet<Dictionary<string, TestVal>>("d", out Dictionary<string, TestVal> val),
                Is.True
            );
            Assert.That(val["a1"], Is.EqualTo(TestVal.Third));
            Assert.That(val["a2"], Is.EqualTo(TestVal.First));
            Assert.That(val["a3"], Is.EqualTo(TestVal.Second));
        }

        [Test]
        public void TryGetValue_CanReadDouble()
        {
            var obj = DynamicJson.Parse("{'d': 25.5}".Replace("'", "\"")) as DynamicJson;
            Assert.That(obj.TryGetValue("d", out double val), Is.True);
            Assert.That(val, Is.EqualTo(25.5));
        }
    }
}
