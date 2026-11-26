using System.Collections.Generic;
using System.Threading.Tasks;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// todo_write accepts the whole list or rejects the call; a rejected call keeps the previous list.
    /// </summary>
    [TestFixture]
    public class TodoWriteTests
    {
        private TodoWrite _tool;

        [SetUp]
        public void SetUp()
        {
            _tool = new TodoWrite();
        }

        private static Dictionary<string, object> Params(object items)
        {
            return new Dictionary<string, object> { [TodoWrite.ItemsParameter] = items };
        }

        private static object Item(string text, string status)
        {
            return new Dictionary<string, object>
            {
                [TodoWrite.TextField] = text,
                [TodoWrite.StatusField] = status
            };
        }

        private static object[] Items(params object[] items)
        {
            return items;
        }

        private static TodoWriteResponse Response(object result)
        {
            var response = result as TodoWriteResponse;
            Assert.That(response, Is.Not.Null, "the tool must always answer with TodoWriteResponse");
            return response;
        }

        private Task<object> Run(object items)
        {
            return _tool.ExecuteAsync(Params(items));
        }

        [Test]
        public void ToolInfo_IsReadOnlyAndDescribesItems()
        {
            Assert.That(_tool.ToolName, Is.EqualTo("todo_write"));
            Assert.That(_tool.AccessLevel, Is.EqualTo(ToolAccessLevel.ReadOnly));

            var info = _tool.GetToolInfo();
            Assert.That(info.Parameters.Required, Does.Contain(TodoWrite.ItemsParameter));
            Assert.That(info.Parameters.Properties.ContainsKey(TodoWrite.ItemsParameter), Is.True);
        }

        [Test]
        public async Task ExecuteAsync_ValidList_ReturnsNormalizedItems()
        {
            var result = await Run(Items(
                Item("Find where X is parsed", "DONE"),
                Item("Fix the parser", "in_progress"),
                Item("Build and run tests", "pending"))).ConfigureAwait(false);

            var response = Response(result);
            Assert.That(response.Success, Is.True);
            Assert.That(response.Items.Count, Is.EqualTo(3));
            Assert.That(response.Items[0].Text, Is.EqualTo("Find where X is parsed"));
            Assert.That(response.Items[0].Status, Is.EqualTo(TodoWrite.StatusDone));
            Assert.That(response.Items[1].Status, Is.EqualTo(TodoWrite.StatusInProgress));
            Assert.That(response.Items[2].Status, Is.EqualTo(TodoWrite.StatusPending));
            Assert.That(response.Summary, Is.EqualTo("1/3 done; in progress: Fix the parser"));

            Assert.That(_tool.Current.Count, Is.EqualTo(3));
        }

        [Test]
        public async Task ExecuteAsync_AllDone_SaysAllDone()
        {
            var result = await Run(Items(
                Item("Read the file", "done"),
                Item("Fix the parser", "done"))).ConfigureAwait(false);

            Assert.That(Response(result).Summary, Is.EqualTo("2/2 done; all done"));
        }

        [Test]
        public async Task ExecuteAsync_TextIsTrimmed()
        {
            var result = await Run(Items(Item("   Fix the parser  ", "pending"))).ConfigureAwait(false);

            Assert.That(Response(result).Items[0].Text, Is.EqualTo("Fix the parser"));
        }

        [Test]
        public async Task ExecuteAsync_ItemsAsJsonString_IsParsed()
        {
            var result = await Run(
                "[{\"text\":\"Fix the parser\",\"status\":\"in_progress\"},{\"text\":\"Build\",\"status\":\"pending\"}]")
                .ConfigureAwait(false);

            var response = Response(result);
            Assert.That(response.Success, Is.True);
            Assert.That(response.Items.Count, Is.EqualTo(2));
            Assert.That(_tool.Current[1].Text, Is.EqualTo("Build"));
        }

        [Test]
        public async Task ExecuteAsync_EmptyArray_ClearsTheList()
        {
            await Run(Items(Item("Fix the parser", "pending"))).ConfigureAwait(false);

            var response = Response(await Run(new object[0]).ConfigureAwait(false));

            Assert.That(response.Success, Is.True);
            Assert.That(response.Items, Is.Empty);
            Assert.That(_tool.Current, Is.Empty);
            Assert.That(response.Summary, Is.EqualTo("no items (list cleared)"));
        }

        [Test]
        public async Task ExecuteAsync_NullParameters_ReturnsError()
        {
            var response = Response(await _tool.ExecuteAsync(null).ConfigureAwait(false));

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Does.Contain("items is required"));
        }

        [Test]
        public async Task ExecuteAsync_MissingItems_ReturnsError()
        {
            var response = Response(await _tool.ExecuteAsync(new Dictionary<string, object>()).ConfigureAwait(false));

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Does.Contain("items is required"));
        }

        [Test]
        public async Task ExecuteAsync_MoreThanMaxItems_ReturnsError()
        {
            var items = new List<object>();
            for (int i = 0; i < TodoWrite.MaxItems + 1; i++)
                items.Add(Item("step " + i, "pending"));

            var response = Response(await Run(items).ConfigureAwait(false));

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Does.Contain("at most 20 items"));
            Assert.That(response.ErrorMessage, Does.Contain("21 were sent"));
            Assert.That(_tool.Current, Is.Empty);
        }

        [Test]
        public async Task ExecuteAsync_TwoInProgress_ReturnsError()
        {
            var response = Response(await Run(Items(
                Item("Fix the parser", "in_progress"),
                Item("Build and run tests", "in_progress"))).ConfigureAwait(false));

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Does.Contain("at most one item can be in_progress"));
        }

        [Test]
        public async Task ExecuteAsync_UnknownStatus_ReturnsError()
        {
            var response = Response(await Run(Items(Item("Fix the parser", "finished"))).ConfigureAwait(false));

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Does.Contain("is not one of pending, in_progress, done"));
        }

        [Test]
        public async Task ExecuteAsync_MissingStatus_ReturnsError()
        {
            var item = new Dictionary<string, object> { [TodoWrite.TextField] = "Fix the parser" };

            var response = Response(await Run(Items(item)).ConfigureAwait(false));

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Does.Contain("has no status"));
        }

        [Test]
        public async Task ExecuteAsync_EmptyText_ReturnsError()
        {
            var response = Response(await Run(Items(Item("   ", "pending"))).ConfigureAwait(false));

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Does.Contain("text is empty after trimming"));
        }

        [Test]
        public async Task ExecuteAsync_TextTooLong_ReturnsError()
        {
            var response = Response(await Run(Items(
                Item(new string('x', TodoWrite.MaxTextLength + 1), "pending"))).ConfigureAwait(false));

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Does.Contain("at most 200"));
        }

        [Test]
        public async Task ExecuteAsync_DuplicateText_ReturnsError()
        {
            var response = Response(await Run(Items(
                Item("Fix the parser", "pending"),
                Item("  fix THE parser ", "done"))).ConfigureAwait(false));

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Does.Contain("duplicate text"));
        }

        [Test]
        public async Task ExecuteAsync_ItemThatIsNotAnObject_ReturnsError()
        {
            var response = Response(await Run(Items("Fix the parser")).ConfigureAwait(false));

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Does.Contain("must be an object with text and status"));
        }

        [Test]
        public async Task ExecuteAsync_ItemsAsPlainString_ReturnsError()
        {
            var response = Response(await Run("Fix the parser").ConfigureAwait(false));

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Does.Contain("must be an array of {text, status} objects"));
        }

        [Test]
        public async Task ExecuteAsync_ItemsAsJObject_ReturnsObjectError()
        {
            var items = JObject.Parse("{\"text\":\"Fix the parser\",\"status\":\"pending\"}");

            var response = Response(await Run(items).ConfigureAwait(false));

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Does.Contain("an object was sent"));
        }

        [Test]
        public async Task ExecuteAsync_ItemsAsDictionary_ReturnsObjectError()
        {
            var response = Response(await Run(Item("Fix the parser", "pending")).ConfigureAwait(false));

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Does.Contain("an object was sent"));
        }

        [Test]
        public void LargestValidList_FitsTheStreamingArgumentsLimit()
        {
            // 20 items of the longest text and status: the guard must not cut a call the tool would accept.
            var items = new List<object>();
            for (int i = 0; i < TodoWrite.MaxItems; i++)
                items.Add(new { text = i.ToString("D2") + new string('x', TodoWrite.MaxTextLength - 2), status = TodoWrite.StatusInProgress });

            string arguments = JsonConvert.SerializeObject(new { items });

            Assert.That(ToolArgumentLimits.GetMaxLength("todo_write"), Is.EqualTo(ToolArgumentLimits.TodoWriteMaxLength));
            Assert.That(arguments.Length, Is.LessThanOrEqualTo(ToolArgumentLimits.TodoWriteMaxLength));
            Assert.That(arguments.Length, Is.GreaterThan(ToolArgumentLimits.SimpleToolMaxLength),
                "the simple-tool limit would cut a valid list");
        }

        [Test]
        public async Task ExecuteAsync_RejectedCall_KeepsThePreviousList()
        {
            await Run(Items(Item("Fix the parser", "in_progress"))).ConfigureAwait(false);

            var response = Response(await Run(Items(
                Item("Fix the parser", "done"),
                Item("Build and run tests", "in_progress"),
                Item("Run the tests", "in_progress"))).ConfigureAwait(false));

            Assert.That(response.Success, Is.False);
            Assert.That(_tool.Current.Count, Is.EqualTo(1));
            Assert.That(_tool.Current[0].Text, Is.EqualTo("Fix the parser"));
            Assert.That(_tool.Current[0].Status, Is.EqualTo(TodoWrite.StatusInProgress));
        }

        [Test]
        public async Task Reset_ClearsTheList()
        {
            await Run(Items(Item("Fix the parser", "pending"))).ConfigureAwait(false);

            _tool.Reset();

            Assert.That(_tool.Current, Is.Empty);
        }

        [Test]
        public void GetProcessingMessage_IsStable()
        {
            Assert.That(_tool.GetProcessingMessage(new Dictionary<string, object>()), Is.EqualTo("Updating task list..."));
        }

        [Test]
        public async Task GetCompletionMessage_Success_ContainsSummary()
        {
            var result = await Run(Items(
                Item("Read the file", "done"),
                Item("Build", "in_progress"))).ConfigureAwait(false);

            Assert.That(_tool.GetCompletionMessage(result), Is.EqualTo("Tasks updated: 1/2 done; in progress: Build."));
        }

        [Test]
        public async Task UiData_AcceptedList_IsTheNormalizedItems()
        {
            var result = await Run(Items(
                Item("  Read the file  ", "done"),
                Item("Fix the parser", "in_progress"),
                Item("Build", "pending"))).ConfigureAwait(false);

            var uiData = Response(result).UiData as IReadOnlyList<TodoItem>;

            Assert.That(uiData, Is.Not.Null, "an accepted list is handed to the UI");
            Assert.That(uiData.Count, Is.EqualTo(3));
            Assert.That(uiData[0].Text, Is.EqualTo("Read the file"));
            Assert.That(uiData[1].Text, Is.EqualTo("Fix the parser"));
            Assert.That(uiData[1].Status, Is.EqualTo(TodoWrite.StatusInProgress));
            Assert.That(uiData[2].Status, Is.EqualTo(TodoWrite.StatusPending));
        }

        [Test]
        public async Task UiData_RejectedCall_IsNull()
        {
            var result = await Run(Items(
                Item("Fix the parser", "in_progress"),
                Item("Fix the parser", "pending"))).ConfigureAwait(false);

            var response = Response(result);

            Assert.That(response.Success, Is.False);
            Assert.That(response.UiData, Is.Null, "a rejected call must not change the widget");
        }

        [Test]
        public void UiData_IsNotInTheModelFacingJson()
        {
            var response = new TodoWriteResponse
            {
                Success = true,
                Items = new[] { new TodoItem { Text = "Read the file", Status = TodoWrite.StatusDone } }
            };

            var json = JsonConvert.SerializeObject(response);

            Assert.That(json, Does.Not.Contain("UiData"));
            Assert.That(json, Does.Not.Contain("ui_data"));
        }
    }
}
