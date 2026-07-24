// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd;
using OpenUsd.Interop;

const string expectedGreeting = "Hello from OpenUsd";
const double expectedAnswer = 42.5;

string stagePath = Path.GetFullPath(
    args.Length > 0
        ? args[0]
        : Path.Combine(Environment.CurrentDirectory, "hello-stage.usda"));
string? outputDirectory = Path.GetDirectoryName(stagePath);
if (!string.IsNullOrEmpty(outputDirectory))
{
    Directory.CreateDirectory(outputDirectory);
}

string packagedPluginPath = Path.Combine(AppContext.BaseDirectory, "usd");
if (File.Exists(Path.Combine(packagedPluginPath, "plugInfo.json")))
{
    _ = OpenUsdNativeRuntime.RegisterPlugins(packagedPluginPath);
}

File.Delete(stagePath);
using (UsdStage stage = UsdStage.Create(stagePath))
{
    stage.DefinePrim("/World", "Xform");
    stage.SetDefaultPrim("/World");
    UsdPrim hello = stage.DefinePrim("/World/Hello", "Xform");
    hello.SetString("custom:greeting", expectedGreeting);
    hello.SetDouble("custom:answer", expectedAnswer);
    stage.Save();
}

using (UsdStage stage = UsdStage.Open(stagePath))
{
    UsdPrim hello = stage.GetPrim("/World/Hello");
    string defaultPrim = stage.GetDefaultPrim().Path;
    string greeting = hello.GetString("custom:greeting");
    double answer = hello.GetDouble("custom:answer");
    if (defaultPrim != "/World" ||
        greeting != expectedGreeting ||
        answer != expectedAnswer)
    {
        throw new InvalidOperationException("The saved stage did not round-trip.");
    }

    Console.WriteLine($"Stage: {stagePath}");
    Console.WriteLine($"Default prim: {defaultPrim}");
    Console.WriteLine($"Greeting: {greeting}");
    Console.WriteLine("Answer: 42.5");
}
