using System;
using System.Collections.Generic;
using System.Linq;
using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;
using AssetRipper.Processing;

string path = args.Length > 0 ? args[0] : @"C:\YostarGames\BlueArchive_JP";

Logger.Add(new DiagLogger());
Console.WriteLine($"Loading {path} ...");
FullConfiguration settings = new();
settings.LoadFromDefaultPath();
GameData gd = new ExportHandler(settings).LoadAndProcess(new[] { path }, LocalFileSystem.Instance);
GameBundle root = gd.GameBundle;
Console.WriteLine("Loaded. Analyzing tree shape...\n");

int maxBundleDepth = 0, totalBundles = 0, maxBundleChildren = 0;
string deepestPath = "";
void WalkB(Bundle b, int depth, string p)
{
    totalBundles++;
    if (depth > maxBundleDepth) { maxBundleDepth = depth; deepestPath = p; }
    int children = b.Bundles.Count + b.Collections.Count;
    maxBundleChildren = Math.Max(maxBundleChildren, children);
    foreach (Bundle c in b.Bundles)
        WalkB(c, depth + 1, p + " > " + c.Name);
}
WalkB(root, 0, root.Name);

int collCount = 0, maxAssetsInColl = 0, maxClassesInColl = 0, maxAssetsInTypeGroup = 0;
string biggestGroup = "", biggestColl = "";
foreach (AssetCollection col in root.FetchAssetCollections())
{
    collCount++;
    if (col.Count > maxAssetsInColl) { maxAssetsInColl = col.Count; biggestColl = col.Name; }
    List<IGrouping<string, IUnityObjectBase>> groups = col.GroupBy(a => a.ClassName).ToList();
    maxClassesInColl = Math.Max(maxClassesInColl, groups.Count);
    foreach (IGrouping<string, IUnityObjectBase> g in groups)
    {
        int n = g.Count();
        if (n > maxAssetsInTypeGroup) { maxAssetsInTypeGroup = n; biggestGroup = $"{col.Name} / {g.Key}"; }
    }
}

int rootDirectChildren = root.Bundles.Count + root.Collections.Count;

Console.WriteLine($"Bundle nesting : maxDepth={maxBundleDepth}, totalBundles={totalBundles}, maxBundleChildren={maxBundleChildren}");
Console.WriteLine($"Deepest path   : {deepestPath}");
Console.WriteLine($"ROOT children  : {rootDirectChildren}  (root.Bundles={root.Bundles.Count}, root.Collections={root.Collections.Count})");
Console.WriteLine($"Collections    : count={collCount}, maxAssetsInColl={maxAssetsInColl} ({biggestColl}), maxClassesInColl={maxClassesInColl}");
Console.WriteLine($"Biggest group  : {biggestGroup} = {maxAssetsInTypeGroup}");

sealed class DiagLogger : ILogger
{
    public void Log(LogType type, LogCategory category, string message) { }
    public void BlankLine(int numLines) { }
}
