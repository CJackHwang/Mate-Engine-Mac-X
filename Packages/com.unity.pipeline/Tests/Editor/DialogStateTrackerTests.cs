using System;
using System.Linq;
using NUnit.Framework;
using Unity.Pipeline;

namespace Unity.Pipeline.Tests.Editor
{
    class DialogStateTrackerTests
    {
        [Test]
        public void NoDialogShown_IsDialogOpen_False_AndCurrentlyOpen_Empty()
        {
            var tracker = new DialogStateTracker();

            Assert.IsFalse(tracker.IsDialogOpen);
            Assert.IsEmpty(tracker.CurrentlyOpen);
        }

        [Test]
        public void OnShown_MakesIsDialogOpen_True_AndAppearsInCurrentlyOpen()
        {
            var tracker = new DialogStateTracker();
            var openedAt = DateTime.UtcNow;

            tracker.OnShown(1, "native", "Title", "Message", new[] { "OK" }, "Warning", openedAt);

            Assert.IsTrue(tracker.IsDialogOpen);
            var open = tracker.CurrentlyOpen.Single();
            Assert.AreEqual(1, open.Id);
            Assert.AreEqual("native", open.Source);
            Assert.AreEqual("Title", open.Title);
            Assert.AreEqual("Message", open.Message);
            CollectionAssert.AreEqual(new[] { "OK" }, open.Buttons);
            Assert.AreEqual("Warning", open.Level);
            Assert.AreEqual(openedAt, open.OpenedAtUtc);
        }

        [Test]
        public void OnDismissed_RemovesFromCurrentlyOpen()
        {
            var tracker = new DialogStateTracker();
            tracker.OnShown(1, "native", "Title", "Message", new[] { "OK" }, "Info", DateTime.UtcNow);

            tracker.OnDismissed(1, DateTime.UtcNow);

            Assert.IsFalse(tracker.IsDialogOpen);
            Assert.IsEmpty(tracker.CurrentlyOpen);
        }

        [Test]
        public void TwoDialogsOpenBeforeEitherDismissed_BothAppearInCurrentlyOpen_DismissingOneLeavesOtherActive()
        {
            var tracker = new DialogStateTracker();
            tracker.OnShown(1, "native", "A", "a", new[] { "OK" }, "Info", DateTime.UtcNow);
            tracker.OnShown(2, "managed", "B", null, null, null, DateTime.UtcNow);

            Assert.AreEqual(2, tracker.CurrentlyOpen.Count);

            tracker.OnDismissed(1, DateTime.UtcNow);

            Assert.IsTrue(tracker.IsDialogOpen);
            var remaining = tracker.CurrentlyOpen.Single();
            Assert.AreEqual(2, remaining.Id);
        }

        [Test]
        public void OnDismissed_WithUnknownId_IsNoOp()
        {
            var tracker = new DialogStateTracker();

            Assert.DoesNotThrow(() => tracker.OnDismissed(999, DateTime.UtcNow));
            Assert.IsFalse(tracker.IsDialogOpen);
        }

        [Test]
        public void EventsSince_IncludesDismissedDialogsOpenedAtOrAfterTimestamp()
        {
            var tracker = new DialogStateTracker();
            var beforeCommand = DateTime.UtcNow;
            var openedAt = beforeCommand.AddMilliseconds(10);
            tracker.OnShown(1, "native", "Title", "Message", new[] { "OK" }, "Info", openedAt);
            tracker.OnDismissed(1, openedAt.AddMilliseconds(5));

            var events = tracker.EventsSince(beforeCommand);

            var found = events.Single();
            Assert.AreEqual(1, found.Id);
            Assert.IsTrue(found.DismissedAtUtc.HasValue);
        }

        [Test]
        public void EventsSince_ExcludesDialogsOpenedBeforeTheTimestamp()
        {
            var tracker = new DialogStateTracker();
            var openedAt = DateTime.UtcNow;
            tracker.OnShown(1, "native", "Title", "Message", new[] { "OK" }, "Info", openedAt);
            tracker.OnDismissed(1, openedAt.AddMilliseconds(5));

            var events = tracker.EventsSince(openedAt.AddSeconds(1));

            Assert.IsEmpty(events);
        }

        [Test]
        public void EventsSince_IncludesStillOpenDialogs()
        {
            var tracker = new DialogStateTracker();
            var beforeCommand = DateTime.UtcNow;
            tracker.OnShown(1, "native", "Title", "Message", new[] { "OK" }, "Info", beforeCommand.AddMilliseconds(5));

            var events = tracker.EventsSince(beforeCommand);

            var found = events.Single();
            Assert.AreEqual(1, found.Id);
            Assert.IsFalse(found.DismissedAtUtc.HasValue);
        }
    }
}
