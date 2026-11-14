// Build-192: checks for the C# readers of the original game's formats (prototype/Formats).
//   dotnet run --project tests/formats -c Release
//       synthetic archives and tables, no game files needed
//   dotnet run --project tests/formats -c Release -- --install "<install>" [--python <fingerprint.json>] [--sequential]
//       every archive file read and CRC-checked and every table parsed, timed; with --python, compared with what
//       tools/formats_fingerprint.py recorded from the Python readers
//   ... --install "<install>" --python-text <original.json>
//       build-193: the original text built from the install, compared with python tools/game_text.py --install --out
//   ... --install "<install>" --python-rules <rules.json>
//       build-194: the rule sections built from the install, compared with python tools/rules_fingerprint.py
//   ... --install "<install>" --python-models <models.json> [--dump-model Models/Units/Good/Aragorn/G_Aragorn.lmd]
//       build-195: every mesh, skeleton, weight and model file parsed, compared with python tools/models_fingerprint.py
//   ... --install "<install>" --python-resolve <resolve.json> [--dump-resolved MODEL_G_ARAGORN]
//       build-196: every Models record resolved, compared with python tools/resolve_fingerprint.py
//   ... --install "<install>" --python-gltf <gltf.json> [--dump-gltf MODEL_G_ARAGORN]
//       build-197: every model the content pack exports built as glTF, compared with python tools/gltf_fingerprint.py
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
int passed = 0;
void Check(bool value,string name)
{
    if (!value) throw new Exception("FAILED: "+name);
    passed++;
    Console.WriteLine(name);
}

SyntheticChecks.Run(Check);
if (Array.IndexOf(args,"--install") is int install && install >= 0)
{
    int python = Array.IndexOf(args,"--python");
    InstallChecks.Run(args[install+1],python >= 0 ? args[python+1] : null,args.Contains("--sequential"),Check);
    if (Array.IndexOf(args,"--python-text") is int text && text >= 0) TextChecks.Install(args[install+1],args[text+1],Check);
    if (Array.IndexOf(args,"--python-rules") is int rules && rules >= 0) RulesChecks.Install(args[install+1],args[rules+1],RepositoryRoot(),Check);
    if (Array.IndexOf(args,"--python-models") is int models && models >= 0) ModelChecks.Install(args[install+1],args[models+1],Check);
    if (Array.IndexOf(args,"--dump-model") is int dump && dump >= 0) ModelChecks.Dump(args[install+1],args[dump+1]);
    if (Array.IndexOf(args,"--python-resolve") is int resolve && resolve >= 0) ResolveChecks.Install(args[install+1],args[resolve+1],Check);
    if (Array.IndexOf(args,"--dump-resolved") is int resolved && resolved >= 0) ResolveChecks.Dump(args[install+1],args[resolved+1]);
    if (Array.IndexOf(args,"--python-gltf") is int gltf && gltf >= 0) GltfChecks.Install(args[install+1],args[gltf+1],Check);
    if (Array.IndexOf(args,"--dump-gltf") is int dumpGltf && dumpGltf >= 0) GltfChecks.Dump(args[install+1],args[dumpGltf+1]);
}
Console.WriteLine($"{passed} format checks passed");

// The folder the AI players' paths are given relative to, as tools/ uses it: the repository.
static string RepositoryRoot()
{
    for (var folder = new DirectoryInfo(Directory.GetCurrentDirectory()); folder != null; folder = folder.Parent)
        if (File.Exists(Path.Combine(folder.FullName,"tools","rules_fingerprint.py"))) return folder.FullName;
    return Directory.GetCurrentDirectory();
}
