using Bloom;
using NUnit.Framework;

namespace BloomTests
{
	[TestFixture]
	public class ExperimentalFeaturesTests
	{
		/// <summary>
		/// Test the SetValue and IsFeatureEnabled methods as well as the
		/// TokensOfEnabledFeatures property.
		/// </summary>
		[Test]
		public void SetValueWorksProperly()
		{
			// In the test setup, this setting always starts out empty (the default value).
			Assert.AreEqual("", ExperimentalFeatures.TokensOfEnabledFeatures);
			Assert.IsFalse(ExperimentalFeatures.IsFeatureEnabled("testing"));

			ExperimentalFeatures.SetValue("testing", true);
			Assert.IsTrue(ExperimentalFeatures.IsFeatureEnabled("testing"));
			Assert.AreEqual("testing", ExperimentalFeatures.TokensOfEnabledFeatures);

			// setting more than once should not change the stored value
			ExperimentalFeatures.SetValue("testing", true);
			Assert.IsTrue(ExperimentalFeatures.IsFeatureEnabled("testing"));
			Assert.AreEqual("testing", ExperimentalFeatures.TokensOfEnabledFeatures);

			ExperimentalFeatures.SetValue("testing", false);
			Assert.IsFalse(ExperimentalFeatures.IsFeatureEnabled("testing"));
			Assert.AreEqual("", ExperimentalFeatures.TokensOfEnabledFeatures);

			ExperimentalFeatures.SetValue(ExperimentalFeatures.kExperimentalSourceBooks, true);
			ExperimentalFeatures.SetValue(ExperimentalFeatures.kTeamCollections, true);
			ExperimentalFeatures.SetValue(ExperimentalFeatures.kExperimentalSourceBooks, true);
			ExperimentalFeatures.SetValue(ExperimentalFeatures.kTeamCollections, true);
			Assert.IsTrue(ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kExperimentalSourceBooks));
			Assert.IsTrue(ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kTeamCollections));
			Assert.AreEqual(ExperimentalFeatures.kExperimentalSourceBooks + ","+ ExperimentalFeatures.kTeamCollections,
				ExperimentalFeatures.TokensOfEnabledFeatures);

			ExperimentalFeatures.SetValue(ExperimentalFeatures.kExperimentalSourceBooks, false);
			Assert.IsFalse(ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kExperimentalSourceBooks));
			Assert.IsTrue(ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kTeamCollections));
			Assert.AreEqual(ExperimentalFeatures.kTeamCollections, ExperimentalFeatures.TokensOfEnabledFeatures);

			ExperimentalFeatures.SetValue(ExperimentalFeatures.kTeamCollections, false);
			Assert.IsFalse(ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kExperimentalSourceBooks));
			Assert.IsFalse(ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kTeamCollections));
			Assert.AreEqual("", ExperimentalFeatures.TokensOfEnabledFeatures);
		}
	}
}
