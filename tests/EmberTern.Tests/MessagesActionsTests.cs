using System.Collections.Generic;
using System.Threading.Tasks;
using EmberTern.App.Controls;
using EmberTern.App.ViewModels;
using Xunit;

namespace EmberTern.Tests;

public class MessagesActionsTests
{
    private static QueryMessageViewModel Msg(string text) => new(MessageSeverity.Info, text);

    [Fact]
    public void BuildMessagesClipboardText_TabSeparatesTimestampAndText_OnePerLine()
    {
        var text = MainWindowViewModel.BuildMessagesClipboardText(new[] { Msg("first"), Msg("second") });
        var lines = text.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.EndsWith("\tfirst", lines[0]);
        Assert.Contains('\t', lines[0]);           // timestamp<TAB>text
        Assert.EndsWith("\tsecond", lines[1]);
    }

    [Fact]
    public void BuildMessagesClipboardText_Empty_IsEmptyString()
        => Assert.Equal("", MainWindowViewModel.BuildMessagesClipboardText(new List<QueryMessageViewModel>()));
}
