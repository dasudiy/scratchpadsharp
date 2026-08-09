using System;
using System.Linq;
using System.ComponentModel;
using System.Data.Common;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using ScratchpadSharp.Core.Database;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Modules;

public sealed class ConnectionStringFieldDescriptor
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public required Type PropertyType { get; init; }
    public required ConnectionStringFieldEditor Editor { get; init; }
    public bool IsCommon { get; init; }
    public IReadOnlyList<string> EnumOptions { get; init; } = [];
    public bool IsBoolean => Editor == ConnectionStringFieldEditor.CheckBox;
    public bool IsEnum => Editor == ConnectionStringFieldEditor.ComboBox;
    public bool IsPassword => Editor == ConnectionStringFieldEditor.Password;
    public bool IsFileBrowse => Editor == ConnectionStringFieldEditor.FileBrowse;
    public bool UseConnectionStringKeyword { get; init; }
    public bool UseComboBoxEditor => Editor == ConnectionStringFieldEditor.ComboBox;
    public bool UseTextBoxEditor => Editor == ConnectionStringFieldEditor.Text;
    public object? Value { get; set; }
}

/// <summary>
/// Curated connection forms per provider. Whitelisted keys + explicit editor types.
/// Builders are used only for parse/build and test connection.
/// </summary>
public static class ConnectionStringBuilderFactory
{
    private sealed record FieldSpec(
        string Key,
        string DisplayName,
        bool IsCommon,
        ConnectionStringFieldEditor Editor,
        string[]? ComboOptions = null,
        bool UseConnectionStringKeyword = false,
        string? KeywordDefault = null);

    private static readonly FieldSpec[] SqliteFields =
    [
        new("DataSource", "Database file", true, ConnectionStringFieldEditor.FileBrowse),
        new("Mode", "Mode", false, ConnectionStringFieldEditor.ComboBox,
            ["ReadWrite", "ReadOnly", "Memory"]),
        new("Cache", "Cache", false, ConnectionStringFieldEditor.ComboBox,
            ["Default", "Shared", "Private"])
    ];

    private static readonly FieldSpec[] SqlServerCommonFields =
    [
        new("DataSource", "Server address", true, ConnectionStringFieldEditor.Text),
        new("InitialCatalog", "Database", true, ConnectionStringFieldEditor.Text),
        new("IntegratedSecurity", "Windows authentication", true, ConnectionStringFieldEditor.CheckBox),
        new("UserID", "User name", true, ConnectionStringFieldEditor.Text),
        new("Password", "Password", true, ConnectionStringFieldEditor.Password),
        new("Encrypt", "Encrypt", true, ConnectionStringFieldEditor.ComboBox,
            ["True", "False", "Strict"],
            UseConnectionStringKeyword: true,
            KeywordDefault: "True"),
        new("TrustServerCertificate", "Trust server certificate", true, ConnectionStringFieldEditor.CheckBox)
    ];

    private static readonly FieldSpec[] SqlServerAdvancedFields =
    [
        new("ApplicationName", "Application name", false, ConnectionStringFieldEditor.Text),
        new("ConnectTimeout", "Connect timeout (seconds)", false, ConnectionStringFieldEditor.Text),
        new("MultipleActiveResultSets", "Multiple active result sets", false, ConnectionStringFieldEditor.CheckBox)
    ];

    public static DbConnectionStringBuilder Create(string providerId, string? connectionString = null)
    {
        DbConnectionStringBuilder builder = providerId switch
        {
            DatabaseProviderIds.Sqlite => new SqliteConnectionStringBuilder(),
            DatabaseProviderIds.SqlServer => new SqlConnectionStringBuilder(),
            _ => throw new ArgumentException($"Unsupported database provider: {providerId}", nameof(providerId))
        };

        if (!string.IsNullOrWhiteSpace(connectionString))
            builder.ConnectionString = connectionString;

        return builder;
    }

    public static DbConnectionStringBuilder CreateEmpty(string providerId) => Create(providerId);

    private static IReadOnlyList<FieldSpec> GetFieldSpecs(string providerId) =>
        providerId switch
        {
            DatabaseProviderIds.Sqlite => SqliteFields,
            DatabaseProviderIds.SqlServer => SqlServerCommonFields.Concat(SqlServerAdvancedFields).ToArray(),
            _ => Array.Empty<FieldSpec>()
        };

    public static List<ConnectionStringFieldDescriptor> GetFields(string providerId, DbConnectionStringBuilder builder)
    {
        var specs = GetFieldSpecs(providerId);
        var props = TypeDescriptor.GetProperties(builder);
        var fields = new List<ConnectionStringFieldDescriptor>();

        foreach (var spec in specs)
        {
            var pd = props[spec.Key];
            if (pd == null)
                continue;

            var rawValue = spec.UseConnectionStringKeyword
                ? ReadConnectionStringKeyword(builder, spec.Key, spec.KeywordDefault)
                : pd.GetValue(builder);
            var comboOptions = spec.ComboOptions ?? [];

            fields.Add(new ConnectionStringFieldDescriptor
            {
                Key = spec.Key,
                DisplayName = spec.DisplayName,
                Description = pd.Description,
                PropertyType = pd.PropertyType,
                Editor = spec.Editor,
                IsCommon = spec.IsCommon,
                UseConnectionStringKeyword = spec.UseConnectionStringKeyword,
                EnumOptions = comboOptions,
                Value = FormatFieldValue(rawValue, spec.Editor)
            });
        }

        return fields;
    }

    public static void ApplyFields(DbConnectionStringBuilder builder, IEnumerable<ConnectionStringFieldDescriptor> fields)
    {
        var props = TypeDescriptor.GetProperties(builder);
        foreach (var field in fields)
        {
            var pd = props[field.Key];
            if (pd == null)
                continue;

            try
            {
                if (field.UseConnectionStringKeyword)
                {
                    if (field.Value is string kw && string.IsNullOrWhiteSpace(kw))
                        continue;

                    var keywordValue = field.Value?.ToString() ?? string.Empty;
                    if (field.Editor == ConnectionStringFieldEditor.ComboBox &&
                        field.EnumOptions.Count > 0 &&
                        !field.EnumOptions.Any(o =>
                            string.Equals(o, keywordValue, StringComparison.OrdinalIgnoreCase)))
                        throw new ArgumentException($"Value '{keywordValue}' is not allowed.");

                    builder[field.Key] = keywordValue;
                    continue;
                }

                if (field.Editor == ConnectionStringFieldEditor.ComboBox &&
                    field.Value is string s && string.IsNullOrWhiteSpace(s))
                    continue;

                var converted = ConvertFieldValue(field, pd.PropertyType);
                pd.SetValue(builder, converted);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Invalid value for '{field.DisplayName}': {ex.Message}", ex);
            }
        }
    }

    public static bool TryParseConnectionString(string providerId, string connectionString,
        out DbConnectionStringBuilder? builder, out string? error)
    {
        try
        {
            builder = Create(providerId, connectionString);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            builder = null;
            error = ex.Message;
            return false;
        }
    }

    private static object? FormatFieldValue(object? rawValue, ConnectionStringFieldEditor editor)
    {
        if (rawValue == null)
            return editor == ConnectionStringFieldEditor.ComboBox ? string.Empty : rawValue;

        if (editor == ConnectionStringFieldEditor.ComboBox)
            return rawValue.ToString();

        return rawValue;
    }

    private static object? ConvertFieldValue(ConnectionStringFieldDescriptor field, Type targetType)
    {
        var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        var value = field.Value;

        if (value == null)
            return effectiveType.IsValueType ? Activator.CreateInstance(effectiveType) : null;

        if (effectiveType.IsInstanceOfType(value))
            return value;

        var stringValue = value.ToString() ?? string.Empty;

        switch (field.Editor)
        {
            case ConnectionStringFieldEditor.CheckBox:
                if (value is bool b)
                    return b;
                return bool.TryParse(stringValue, out var parsed) && parsed;

            case ConnectionStringFieldEditor.ComboBox:
                return ParseComboValue(stringValue, effectiveType, field.EnumOptions);

            case ConnectionStringFieldEditor.Text:
            case ConnectionStringFieldEditor.Password:
            case ConnectionStringFieldEditor.FileBrowse:
                if (effectiveType == typeof(int))
                    return int.TryParse(stringValue, out var i) ? i : 0;
                if (effectiveType == typeof(uint))
                    return uint.TryParse(stringValue, out var u) ? u : 0u;
                return stringValue;

            default:
                throw new InvalidOperationException($"Unsupported editor: {field.Editor}");
        }
    }

    private static object? ParseComboValue(string stringValue, Type targetType, IReadOnlyList<string> allowedOptions)
    {
        if (allowedOptions.Count > 0 &&
            !allowedOptions.Any(o => string.Equals(o, stringValue, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Value '{stringValue}' is not allowed.");

        if (targetType.IsEnum)
            return Enum.Parse(targetType, stringValue, ignoreCase: true);

        var staticField = targetType.GetField(stringValue,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
        if (staticField != null && staticField.FieldType == targetType)
            return staticField.GetValue(null);

        throw new ArgumentException($"Cannot parse '{stringValue}' as {targetType.Name}.");
    }

    private static object? ReadConnectionStringKeyword(
        DbConnectionStringBuilder builder,
        string key,
        string? keywordDefault)
    {
        if (builder.TryGetValue(key, out var value) && value != null)
            return value.ToString();

        return keywordDefault ?? string.Empty;
    }
}
