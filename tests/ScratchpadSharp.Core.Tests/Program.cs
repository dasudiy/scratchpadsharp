using ScratchpadSharp.Core.Tests;

var failures = ConnectionStringBuilderFactoryTests.RunAll();
failures += EfScaffoldGeneratorTests.RunAll();
failures += NuGetPackageAssetResolverTests.RunAll();
failures += ScriptIsolationTests.RunAll();
failures += EfSqlServerScriptTests.RunAll();
if (failures > 0)
{
    Console.Error.WriteLine($"{failures} test(s) failed.");
    return failures;
}

Console.WriteLine("All Core tests passed.");
return 0;
