// Build-192: checks for the C# readers of the original game's formats (prototype/Formats).
//   dotnet run --project tests/formats -c Release
//       synthetic archives and tables, no game files needed
//   dotnet run --project tests/formats -c Release -- --install "<install>" [--python <fingerprint.json>] [--sequential]
//       every archive file read and CRC-checked and every table parsed, timed; with --python, compared with what
//       tools/formats_fingerprint.py recorded from the Python readers
//   ... --install "<install>" --python-text <original.json>
//       build-193: the original text built from the install, compared with python tools/game_text.py --install --out
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
}
Console.WriteLine($"{passed} format checks passed");
