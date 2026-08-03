using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests.Models;

public class SubItemMigrationTests
{
    [Fact]
    public void CreateTable_ShouldAddSupportUrlToAnExistingSubscriptionTable()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"svoray-subitem-{Guid.NewGuid():N}.db");
        try
        {
            using var database = new SQLiteConnection(databasePath, false);
            database.Execute(
                "CREATE TABLE SubItem ("
                + "Id varchar PRIMARY KEY, Remarks varchar, Url varchar, MoreUrl varchar, "
                + "Enabled integer, UserAgent varchar, Sort integer, Filter varchar, "
                + "AutoUpdateInterval integer, UpdateTime integer, ConvertTarget varchar, "
                + "PrevProfile varchar, NextProfile varchar, PreSocksPort integer, Memo varchar)");

            database.CreateTable<SubItem>();

            database.GetTableInfo(nameof(SubItem))
                .Select(column => column.Name)
                .Should().Contain(nameof(SubItem.SupportUrl));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
