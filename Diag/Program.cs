using System;
using System.Collections.Generic;
using System.Linq;
using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.Assets.Metadata;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;
using AssetRipper.Processing;
using AssetRipper.SourceGenerated.Classes.ClassID_128; // IFont
using AssetRipper.SourceGenerated.Extensions;          // GetFontExtension

string path = args.Length > 0 ? args[0] : @"C:\YostarGames\BlueArchive_JP";

Logger.Add(new DiagLogger());
Console.WriteLine($"Loading {path} ...");
FullConfiguration settings = new();
settings.LoadFromDefaultPath();
GameData gd = new ExportHandler(settings).LoadAndProcess(new[] { path }, LocalFileSystem.Instance);
Console.WriteLine("Loaded. Font analysis:\n");

int fontTotal = 0, fontWithData = 0;
List<string> examples = new();
foreach (AssetCollection col in gd.GameBundle.FetchAssetCollections())
{
    foreach (IUnityObjectBase a in col)
    {
        if (a is IFont f)
        {
            fontTotal++;
            if (f.FontData.Length > 0)
            {
                fontWithData++;
                if (examples.Count < 12)
                    examples.Add($"  Font '{f.GetBestName()}'  {f.GetFontExtension()}  {f.FontData.Length / 1024}KB");
            }
        }
    }
}

int refCount = 0;
Dictionary<string, int> fontNamedByClass = new();
List<string> refExamples = new();
foreach (AssetCollection col in gd.GameBundle.FetchAssetCollections())
{
    foreach (IUnityObjectBase a in col)
    {
        if (a is IFont)
            continue;
        bool likely = a.GetBestName().Contains("font", StringComparison.OrdinalIgnoreCase)
                      || a.ClassName.Contains("Font", StringComparison.OrdinalIgnoreCase);
        if (!likely)
            continue;
        fontNamedByClass[a.ClassName] = fontNamedByClass.GetValueOrDefault(a.ClassName) + 1;
        foreach ((string _, PPtr pptr) in a.FetchDependencies())
        {
            if (a.Collection.TryGetAsset(pptr, out IUnityObjectBase? dep) && dep is IFont rf && rf.FontData.Length > 0)
            {
                refCount++;
                if (refExamples.Count < 12)
                    refExamples.Add($"  {a.ClassName} '{a.GetBestName()}' -> embedded '{rf.GetBestName()}'");
                break;
            }
        }
    }
}

Console.WriteLine($"IFont assets: total={fontTotal}, withEmbeddedData(extractable as TTF/OTF)={fontWithData}");
foreach (string e in examples) Console.WriteLine(e);
Console.WriteLine($"\nFont-named NON-IFont assets by class (likely TMP etc.):");
foreach (KeyValuePair<string, int> kv in fontNamedByClass.OrderByDescending(k => k.Value).Take(15))
    Console.WriteLine($"  {kv.Key}: {kv.Value}");
Console.WriteLine($"\nFont-named assets referencing an EMBEDDED font (also extractable): {refCount}");
foreach (string e in refExamples) Console.WriteLine(e);

sealed class DiagLogger : ILogger
{
    public void Log(LogType type, LogCategory category, string message) { }
    public void BlankLine(int numLines) { }
}
