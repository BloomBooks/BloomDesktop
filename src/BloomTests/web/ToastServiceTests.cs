using System;
using System.Reflection;
using Bloom.web;
using NUnit.Framework;

namespace BloomTests.web
{
    [TestFixture]
    public class ToastServiceTests
    {
        private static object GetCallbackActionsDictionary()
        {
            return typeof(ToastService)
                .GetField("s_callbackActions", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null);
        }

        private static void ClearRegisteredCallbacks()
        {
            var callbackActions = GetCallbackActionsDictionary();
            callbackActions.GetType().GetMethod("Clear")?.Invoke(callbackActions, null);
        }

        private static int GetRegisteredCallbackCount()
        {
            var callbackActions = GetCallbackActionsDictionary();
            return (int)callbackActions.GetType().GetProperty("Count")?.GetValue(callbackActions);
        }

        private static Action GetRegisteredCallback(string callbackId)
        {
            var callbackActions = GetCallbackActionsDictionary();
            var tryGetValueArgs = new object[] { callbackId, null };
            var found = (bool)
                callbackActions
                    .GetType()
                    .GetMethod("TryGetValue")
                    ?.Invoke(callbackActions, tryGetValueArgs);

            Assert.That(found, Is.True, $"Expected callback '{callbackId}' to be registered.");

            var callbackRecord = tryGetValueArgs[1];
            return (Action)callbackRecord.GetType().GetProperty("Action")?.GetValue(callbackRecord);
        }

        [SetUp]
        public void SetUp()
        {
            ClearRegisteredCallbacks();
        }

        [TearDown]
        public void TearDown()
        {
            ClearRegisteredCallbacks();
        }

        [Test]
        public void ShowToast_SameToastId_ReusesSingleRegisteredCallback()
        {
            var firstCallbackRan = false;
            var secondCallbackRan = false;

            ToastService.ShowToast(
                text: "Repeated persistent toast",
                action: new ToastAction
                {
                    Label = "Do It",
                    Callback = () => firstCallbackRan = true,
                },
                toastId: "repeated-persistent-toast"
            );

            ToastService.ShowToast(
                text: "Repeated persistent toast",
                action: new ToastAction
                {
                    Label = "Do It",
                    Callback = () => secondCallbackRan = true,
                },
                toastId: "repeated-persistent-toast"
            );

            Assert.That(GetRegisteredCallbackCount(), Is.EqualTo(1));

            GetRegisteredCallback("toast:repeated-persistent-toast")();

            Assert.That(firstCallbackRan, Is.False);
            Assert.That(secondCallbackRan, Is.True);
        }
    }
}
