using System.Linq;
using Microsoft.Data.SqlClient;
using ScratchpadSharp.Core.Database;
using ScratchpadSharp.Core.Modules;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Tests;

public static class ConnectionStringBuilderFactoryTests
{
    public static int RunAll()
    {
        var failures = 0;
        failures += Run(nameof(SqlServer_TextFields_ApplyWithoutError), SqlServer_TextFields_ApplyWithoutError);
        failures += Run(nameof(SqlServer_Encrypt_ComboBox_RoundTrip), SqlServer_Encrypt_ComboBox_RoundTrip);
        failures += Run(nameof(SqlServer_ApplyAllCommonFields), SqlServer_ApplyAllCommonFields);
        failures += Run(nameof(Sqlite_FileBrowse_OnlyNoExtraCombo), Sqlite_FileBrowse_OnlyNoExtraCombo);
        failures += Run(nameof(Sqlite_ModeCache_ComboRoundTrip), Sqlite_ModeCache_ComboRoundTrip);
        return failures;
    }

    private static int Run(string name, Func<bool> test) =>
        test() ? 0 : ReportFail(name);

    private static int ReportFail(string name)
    {
        Console.WriteLine($"FAIL: {name}");
        return 1;
    }

    private static bool SqlServer_TextFields_ApplyWithoutError()
    {
        var builder = ConnectionStringBuilderFactory.CreateEmpty(DatabaseProviderIds.SqlServer);
        var fields = ConnectionStringBuilderFactory.GetFields(DatabaseProviderIds.SqlServer, builder);

        Set(fields, "DataSource", "localhost");
        Set(fields, "InitialCatalog", "MyDb");
        Set(fields, "UserID", "sa");
        Set(fields, "Password", "secret");

        ConnectionStringBuilderFactory.ApplyFields(builder, fields);
        return builder is SqlConnectionStringBuilder sql &&
               sql.DataSource == "localhost" &&
               sql.InitialCatalog == "MyDb" &&
               sql.UserID == "sa" &&
               sql.Password == "secret";
    }

    private static bool SqlServer_Encrypt_ComboBox_RoundTrip()
    {
        foreach (var option in new[] { "True", "False", "Strict" })
        {
            var builder = ConnectionStringBuilderFactory.CreateEmpty(DatabaseProviderIds.SqlServer);
            var fields = ConnectionStringBuilderFactory.GetFields(DatabaseProviderIds.SqlServer, builder);
            var encrypt = fields.First(f => f.Key == "Encrypt");

            if (encrypt.Editor != ConnectionStringFieldEditor.ComboBox)
                return false;

            if (encrypt.EnumOptions.Count == 0)
                return false;

            encrypt.Value = option;
            ConnectionStringBuilderFactory.ApplyFields(builder, fields);

            var roundTrip = ConnectionStringBuilderFactory.GetFields(DatabaseProviderIds.SqlServer, builder);
            var encryptRoundTrip = roundTrip.First(f => f.Key == "Encrypt");
            if (!string.Equals(encryptRoundTrip.Value?.ToString(), option, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static bool SqlServer_ApplyAllCommonFields()
    {
        var builder = ConnectionStringBuilderFactory.CreateEmpty(DatabaseProviderIds.SqlServer);
        var fields = ConnectionStringBuilderFactory.GetFields(DatabaseProviderIds.SqlServer, builder);

        Set(fields, "DataSource", "127.0.0.1");
        Set(fields, "InitialCatalog", "test");
        Set(fields, "IntegratedSecurity", false);
        Set(fields, "UserID", "user");
        Set(fields, "Password", "pwd");
        Set(fields, "Encrypt", "Strict");
        Set(fields, "TrustServerCertificate", true);

        try
        {
            ConnectionStringBuilderFactory.ApplyFields(builder, fields);
        }
        catch
        {
            return false;
        }

        return builder.ConnectionString.Contains("Encrypt=", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Sqlite_FileBrowse_OnlyNoExtraCombo()
    {
        var builder = ConnectionStringBuilderFactory.CreateEmpty(DatabaseProviderIds.Sqlite);
        var common = ConnectionStringBuilderFactory.GetFields(DatabaseProviderIds.Sqlite, builder)
            .Where(f => f.IsCommon)
            .ToList();

        if (common.Count != 1)
            return false;

        var file = common[0];
        return file.Key == "DataSource" &&
               file.Editor == ConnectionStringFieldEditor.FileBrowse &&
               file.Editor != ConnectionStringFieldEditor.ComboBox;
    }

    private static bool Sqlite_ModeCache_ComboRoundTrip()
    {
        var builder = ConnectionStringBuilderFactory.CreateEmpty(DatabaseProviderIds.Sqlite);
        var fields = ConnectionStringBuilderFactory.GetFields(DatabaseProviderIds.Sqlite, builder);

        var mode = fields.First(f => f.Key == "Mode");
        var cache = fields.First(f => f.Key == "Cache");

        if (mode.Editor != ConnectionStringFieldEditor.ComboBox ||
            cache.Editor != ConnectionStringFieldEditor.ComboBox)
            return false;

        mode.Value = "ReadOnly";
        cache.Value = "Shared";
        ConnectionStringBuilderFactory.ApplyFields(builder, fields);

        var sql = (Microsoft.Data.Sqlite.SqliteConnectionStringBuilder)builder;
        return sql.Mode == Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly &&
               sql.Cache == Microsoft.Data.Sqlite.SqliteCacheMode.Shared;
    }

    private static void Set(List<ConnectionStringFieldDescriptor> fields, string key, object? value)
    {
        var field = fields.First(f => f.Key == key);
        field.Value = value;
    }
}
