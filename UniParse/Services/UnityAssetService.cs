using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using AssetRipper.Assets;
using AssetRipper.Assets.Metadata;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.Modules.Audio;
using AssetRipper.Export.Modules.Models;
using AssetRipper.Export.Modules.Textures;
using AssetRipper.Export.PrimaryContent;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Export.UnityProjects.Shaders; // DummyShaderTextExporter
using AssetRipper.IO.Files;
using AssetRipper.Processing;
using AssetRipper.Processing.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_156; // ITerrainData
using AssetRipper.SourceGenerated.Classes.ClassID_189; // IImageTexture
using AssetRipper.SourceGenerated.Classes.ClassID_21;  // IMaterial
using AssetRipper.SourceGenerated.Classes.ClassID_213; // ISprite
using AssetRipper.SourceGenerated.Classes.ClassID_43;  // IMesh
using AssetRipper.SourceGenerated.Classes.ClassID_48;  // IShader
using AssetRipper.SourceGenerated.Classes.ClassID_49;  // ITextAsset
using AssetRipper.SourceGenerated.Classes.ClassID_74;  // IAnimationClip
using AssetRipper.SourceGenerated.Classes.ClassID_83;  // IAudioClip
using AssetRipper.SourceGenerated.Classes.ClassID_329; // IVideoClip
using AssetRipper.SourceGenerated.Classes.ClassID_128; // IFont
using AssetRipper.SourceGenerated.Extensions;          // CheckAssetIntegrity, GetBestExtension, MaterialExtensions
using AssetRipper.SourceGenerated.Subclasses.UnityTexEnv;
using SharpGLTF.Schema2;
using SharpGLTF.Scenes;

namespace UniParse.Services;

/// <summary>What kind of preview an asset produces.</summary>
public enum PreviewKind { None, Image, Model, Audio, Video, Font }

/// <summary>The result of building a preview for one asset.</summary>
public sealed record PreviewResult
{
    public PreviewKind Kind { get; init; }
    public byte[]? Png { get; init; }
    public MeshGeometry3D? Geometry { get; init; }
    public byte[]? Wav { get; init; }
    public byte[]? Video { get; init; }
    public string VideoExtension { get; init; } = "";
    public byte[]? Font { get; init; }
    public string FontExtension { get; init; } = "";
    public string Info { get; init; } = "";
    public string Message { get; init; } = "";
}

/// <summary>
/// Wraps AssetRipper: loads Unity files and extracts previews (image / 3D model / audio),
/// JSON and text for a single asset. Mirrors AssetRipper's own GUI (AssetAPI).
/// </summary>
public sealed class UnityAssetService
{
    public GameData? GameData { get; private set; }

    public bool IsLoaded => GameData is not null;

    public string ProjectVersion => GameData?.ProjectVersion.ToString() ?? "-";

    public void Load(IReadOnlyList<string> paths)
    {
        FullConfiguration settings = new();
        settings.LoadFromDefaultPath();
        ExportHandler handler = new(settings);
        GameData = handler.LoadAndProcess(paths, LocalFileSystem.Instance);
    }

    public void Reset()
    {
        GameData = null;
        System.GC.Collect();
    }

    // ===================================================================== //
    //  Preview dispatch                                                     //
    // ===================================================================== //

    /// <summary>True if the asset's type can carry an image (cheap, used for tree glyphs).</summary>
    public static bool IsImageLike(IUnityObjectBase asset)
        => asset is IImageTexture or ISprite or ITerrainData or SpriteInformationObject;

    /// <summary>True if the asset has any kind of rich preview (used for tree glyphs).</summary>
    public static bool IsPreviewable(IUnityObjectBase asset)
        => IsImageLike(asset)
           || asset is IMaterial or IMesh or IAudioClip or IShader or IAnimationClip or IVideoClip or IFont
           || asset.ClassName is "VideoPlayer";

    /// <summary>Builds whatever preview the asset supports (image / model / audio).</summary>
    public static PreviewResult BuildPreview(IUnityObjectBase asset)
    {
        if (IsImageLike(asset))
            return BuildImage(asset);

        if (asset is IMaterial material)
        {
            IUnityObjectBase? texture = ResolveMaterialTexture(material);
            if (texture is not null)
            {
                PreviewResult image = BuildImage(texture);
                string label = $"マテリアル → {texture.GetBestName()}";
                return image with { Info = image.Info.Length > 0 ? $"{label}  •  {image.Info}" : label };
            }
            return None("このマテリアルにはテクスチャ参照がありません（JSON を参照してください）。");
        }

        if (asset is IMesh)
        {
            MeshGeometry3D? geometry = BuildMeshGeometry(asset, out string info);
            return geometry is not null
                ? new PreviewResult { Kind = PreviewKind.Model, Geometry = geometry, Info = info }
                : None("メッシュをプレビューできませんでした（.glb で保存できます）。");
        }

        if (asset is IAudioClip)
        {
            (byte[]? wav, string info, string message) = DecodeAudioWav(asset);
            return wav is not null
                ? new PreviewResult { Kind = PreviewKind.Audio, Wav = wav, Info = info }
                : None(message);
        }

        if (asset is IVideoClip videoClip)
            return BuildVideo(videoClip);

        if (asset.ClassName is "VideoPlayer" && ResolveReferencedVideoClip(asset) is { } referencedClip)
            return BuildVideo(referencedClip);

        if (asset is IFont font && font.FontData.Length > 0)
        {
            string ext = font.GetFontExtension();
            return new PreviewResult
            {
                Kind = PreviewKind.Font,
                Font = font.FontData,
                FontExtension = ext,
                Info = $"{asset.GetBestName()}   •   {ext.ToUpperInvariant()}   •   {font.FontData.Length / 1024:N0} KB",
            };
        }

        if (asset is IShader or IAnimationClip or ITextAsset)
            return None("このアセットの内容は左のタブ（テキスト / シェーダー / アニメーション）に表示しています。");

        return None("この種類のアセットには画像/モデル/音声/動画プレビューがありません。");
    }

    private static PreviewResult None(string message)
        => new() { Kind = PreviewKind.None, Message = message };

    // ===================================================================== //
    //  Image (textures / sprites / terrain / material textures)             //
    // ===================================================================== //

    public static bool HasImage(IUnityObjectBase asset) => asset switch
    {
        IImageTexture texture => texture.CheckAssetIntegrity(),
        SpriteInformationObject sio => sio.Texture.CheckAssetIntegrity(),
        ISprite sprite => SpriteConverter.Supported(sprite),
        ITerrainData terrain => terrain.Heightmap.Heights.Count > 0,
        _ => false,
    };

    private static PreviewResult BuildImage(IUnityObjectBase asset)
    {
        if (!HasImage(asset))
            return None(
                "⚠ 画像のピクセルデータが見つかりません。\n\n" +
                "テクスチャの実データはストリーミングリソース（.resS / .resource）にあり、" +
                "現在の読み込み対象では解決できていません。\n\n" +
                "単一ファイルではなく、ゲームのデータフォルダ全体を\n「🗁 フォルダを開く」または D&D で開いてください。");

        try
        {
            DirectBitmap bitmap = GetBitmap(asset);
            if (bitmap.IsEmpty)
                return None("画像をデコードできませんでした（未対応の形式の可能性があります）。");

            using MemoryStream stream = new();
            bitmap.Save(stream, ImageExportFormat.Png);
            return new PreviewResult { Kind = PreviewKind.Image, Png = stream.ToArray(), Info = $"{bitmap.Width} × {bitmap.Height} px" };
        }
        catch
        {
            return None("画像をデコードできませんでした（未対応の形式の可能性があります）。");
        }
    }

    private static DirectBitmap GetBitmap(IUnityObjectBase asset) => asset switch
    {
        IImageTexture texture => TextureConverter.TryConvertToBitmap(texture, out DirectBitmap b) ? b : DirectBitmap.Empty,
        SpriteInformationObject sio => TextureConverter.TryConvertToBitmap(sio.Texture, out DirectBitmap b) ? b : DirectBitmap.Empty,
        ISprite sprite => SpriteConverter.TryConvertToBitmap(sprite, out DirectBitmap b) ? b : DirectBitmap.Empty,
        ITerrainData terrain => TerrainHeatmap.GetBitmap(terrain),
        _ => DirectBitmap.Empty,
    };

    private static IUnityObjectBase? ResolveMaterialTexture(IMaterial material)
    {
        // Prefer the conventional main texture, otherwise the first resolvable texture.
        if (material.TryGetTextureProperty("_MainTex", out IUnityTexEnv? mainTex))
        {
            IUnityObjectBase? main = mainTex.Texture.TryGetAsset(material.Collection);
            if (main is IImageTexture)
                return main;
        }

        foreach (KeyValuePair<AssetRipper.Primitives.Utf8String, IUnityTexEnv> pair in material.GetTextureProperties())
        {
            IUnityObjectBase? texture = pair.Value.Texture.TryGetAsset(material.Collection);
            if (texture is IImageTexture)
                return texture;
        }
        return null;
    }

    /// <summary>Renders an image-bearing asset (texture / sprite / material) to PNG, for saving.</summary>
    public static byte[]? RenderPng(IUnityObjectBase asset) => BuildPreview(asset).Png;

    // ===================================================================== //
    //  Mesh / model                                                         //
    // ===================================================================== //

    public static MeshGeometry3D? BuildMeshGeometry(IUnityObjectBase asset, out string info)
    {
        info = "";
        if (asset is not IMesh mesh)
            return null;

        ModelRoot model;
        try { model = GlbMeshBuilder.Build(mesh).ToGltf2(); }
        catch { return null; }

        Point3DCollection positions = new();
        Vector3DCollection normals = new();
        Int32Collection indices = new();

        foreach (var logicalMesh in model.LogicalMeshes)
        {
            foreach (var primitive in logicalMesh.Primitives)
            {
                var positionAccessor = primitive.GetVertexAccessor("POSITION");
                if (positionAccessor is null)
                    continue;

                IList<System.Numerics.Vector3> verts = positionAccessor.AsVector3Array();
                int baseIndex = positions.Count;
                foreach (System.Numerics.Vector3 v in verts)
                    positions.Add(new Point3D(v.X, v.Y, v.Z));

                var normalAccessor = primitive.GetVertexAccessor("NORMAL");
                if (normalAccessor is not null)
                {
                    foreach (System.Numerics.Vector3 n in normalAccessor.AsVector3Array())
                        normals.Add(new Vector3D(n.X, n.Y, n.Z));
                }

                IList<uint> triangleIndices = primitive.GetIndices();
                if (triangleIndices is { Count: > 0 })
                {
                    foreach (uint index in triangleIndices)
                        indices.Add(baseIndex + (int)index);
                }
                else
                {
                    for (int i = 0; i < verts.Count; i++)
                        indices.Add(baseIndex + i);
                }
            }
        }

        if (positions.Count == 0 || indices.Count == 0)
            return null;

        MeshGeometry3D geometry = new() { Positions = positions, TriangleIndices = indices };
        if (normals.Count == positions.Count)
            geometry.Normals = normals;
        geometry.Freeze();

        info = $"{positions.Count:N0} 頂点 / {indices.Count / 3:N0} 三角形";
        return geometry;
    }

    /// <summary>Builds a glb (binary glTF) for saving a mesh.</summary>
    public static byte[]? TryGetGlb(IUnityObjectBase asset)
    {
        if (asset is not IMesh mesh)
            return null;
        try
        {
            using MemoryStream stream = new();
            if (GlbWriter.TryWrite(GlbMeshBuilder.Build(mesh), stream, out _))
                return stream.ToArray();
        }
        catch { /* ignore */ }
        return null;
    }

    // ===================================================================== //
    //  Audio                                                                //
    // ===================================================================== //

    private static (byte[]? Wav, string Info, string Message) DecodeAudioWav(IUnityObjectBase asset)
    {
        if (asset is not IAudioClip clip)
            return (null, "", "");

        if (!AudioClipDecoder.TryDecode(clip, out byte[]? data, out string? extension, out string? message))
            return (null, "", message ?? "音声をデコードできませんでした。");

        string name = asset.GetBestName();
        switch (extension)
        {
            case "wav":
                return (data, $"{name}   •   WAV   •   {data.Length / 1024:N0} KB", "");
            case "ogg":
                byte[] wav = AudioConverter.OggToWav(data);
                if (wav.Length > 0)
                    return (wav, $"{name}   •   OGG→WAV   •   {wav.Length / 1024:N0} KB", "");
                return (null, "", $"{name} は OGG です（再生は未対応／「音声を保存」で保存できます）。");
            default:
                return (null, "",
                    $"{name} は {extension.ToUpperInvariant()} 形式です（プレビュー再生は未対応／「音声を保存」で保存できます）。");
        }
    }

    /// <summary>Decodes an audio clip to its native container, for saving.</summary>
    public static (byte[]? Data, string Extension) TryGetAudio(IUnityObjectBase asset)
    {
        if (asset is IAudioClip clip &&
            AudioClipDecoder.TryDecode(clip, out byte[]? data, out string? extension, out _))
        {
            return (data, extension);
        }
        return (null, "bin");
    }

    // ===================================================================== //
    //  Video                                                                //
    // ===================================================================== //

    private static PreviewResult BuildVideo(IVideoClip clip)
    {
        if (!clip.CheckIntegrity() || !clip.TryGetContent(out byte[]? content))
            return None("動画データを取得できません（外部リソース .resource が見つからない可能性があります）。");

        string extension = clip.TryGetExtensionFromPath(out string? e) ? e : "mp4";
        return new PreviewResult
        {
            Kind = PreviewKind.Video,
            Video = content,
            VideoExtension = extension,
            Info = $"{clip.GetBestName()}   •   {extension.ToUpperInvariant()}   •   {content.Length / 1024:N0} KB",
        };
    }

    /// <summary>Finds the first VideoClip referenced by a component (e.g. a VideoPlayer).</summary>
    private static IVideoClip? ResolveReferencedVideoClip(IUnityObjectBase component)
    {
        foreach ((string _, PPtr pptr) in component.FetchDependencies())
        {
            if (component.Collection.TryGetAsset(pptr, out IUnityObjectBase? dependency) && dependency is IVideoClip clip)
                return clip;
        }
        return null;
    }

    /// <summary>Extracts the raw video container for saving (handles VideoClip and VideoPlayer).</summary>
    public static (byte[]? Data, string Extension) TryGetVideo(IUnityObjectBase asset)
    {
        IVideoClip? clip = asset as IVideoClip
            ?? (asset.ClassName is "VideoPlayer" ? ResolveReferencedVideoClip(asset) : null);

        if (clip is not null && clip.TryGetContent(out byte[]? data))
            return (data, clip.TryGetExtensionFromPath(out string? e) ? e : "mp4");
        return (null, "bin");
    }

    /// <summary>Extracts the embedded font file (ttf/otf) for saving.</summary>
    public static (byte[]? Data, string Extension) TryGetFont(IUnityObjectBase asset)
    {
        if (asset is IFont font && font.FontData.Length > 0)
            return (font.FontData, font.GetFontExtension());
        return (null, "ttf");
    }

    // ===================================================================== //
    //  JSON / text                                                          //
    // ===================================================================== //

    public static string GetJson(IUnityObjectBase asset)
    {
        StringWriter writer = new(CultureInfo.InvariantCulture) { NewLine = "\n" };
        asset.WalkStandard(new DefaultJsonWalker(writer));
        return writer.ToString();
    }

    /// <summary>The kind of textual content an asset exposes (drives the tab label/extension).</summary>
    public enum TextKind { None, Text, Shader, Animation }

    /// <summary>Returns textual content for text assets, shaders and animation clips.</summary>
    public static (TextKind Kind, string Content) GetTextContent(IUnityObjectBase asset)
    {
        switch (asset)
        {
            case IShader shader:
                return (TextKind.Shader, GetShaderText(shader));
            case IAnimationClip clip:
                return (TextKind.Animation, GetAnimationSummary(clip));
            case ITextAsset { Script_C49.IsEmpty: false } textAsset:
                return (TextKind.Text, textAsset.Script_C49);
            default:
                return (TextKind.None, string.Empty);
        }
    }

    public static string GetTextExtension(IUnityObjectBase asset) => asset switch
    {
        IShader => "shader",
        IAnimationClip => "txt",
        ITextAsset textAsset => textAsset.GetBestExtension() ?? "txt",
        _ => "txt",
    };

    private static string GetShaderText(IShader shader)
    {
        if (shader.Has_Script() && !shader.Script.IsEmpty)
            return shader.Script;

        StringWriter writer = new(CultureInfo.InvariantCulture) { NewLine = "\n" };
        try { DummyShaderTextExporter.ExportShader(shader, writer); }
        catch (Exception ex) { return "// シェーダーを書き出せませんでした:\n// " + ex.Message; }
        return writer.ToString();
    }

    private static string GetAnimationSummary(IAnimationClip clip)
    {
        StringBuilder sb = new();
        sb.AppendLine($"AnimationClip : {clip.GetBestName()}");
        sb.AppendLine($"Legacy        : {clip.GetLegacy()}");
        sb.AppendLine($"Sample Rate   : {clip.SampleRate_C74}");
        sb.AppendLine();

        int position = clip.PositionCurves_C74.Count;
        int rotation = clip.RotationCurves_C74.Count;
        int scale = clip.ScaleCurves_C74.Count;
        int euler = clip.Has_EulerCurves_C74() ? clip.EulerCurves_C74.Count : 0;
        int floats = clip.FloatCurves_C74.Count;
        int pptr = clip.Has_PPtrCurves_C74() ? clip.PPtrCurves_C74.Count : 0;

        sb.AppendLine("Curves:");
        sb.AppendLine($"  Position : {position}");
        sb.AppendLine($"  Rotation : {rotation}");
        sb.AppendLine($"  Scale    : {scale}");
        sb.AppendLine($"  Euler    : {euler}");
        sb.AppendLine($"  Float    : {floats}");
        sb.AppendLine($"  PPtr     : {pptr}");
        sb.AppendLine($"  Total    : {position + rotation + scale + euler + floats + pptr}");
        sb.AppendLine();
        sb.AppendLine("（詳細なキーフレーム・イベントは JSON タブを参照してください）");
        return sb.ToString();
    }
}
