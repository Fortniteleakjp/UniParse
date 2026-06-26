using System;
using System.Collections.ObjectModel;
using AssetRipper.Assets;
using UniParse.Infrastructure;

namespace UniParse.Models;

public enum AssetNodeKind
{
    Bundle,
    Collection,
    TypeGroup,
    Asset,
}

/// <summary>
/// A single node in the left-hand structure tree. Grouping nodes
/// (bundle / collection / type) carry no asset; leaf nodes carry one
/// <see cref="IUnityObjectBase"/>.
/// </summary>
public sealed class AssetTreeNode : ObservableObject
{
    public AssetTreeNode(
        string header,
        AssetNodeKind kind,
        IUnityObjectBase? asset = null,
        string? toolTip = null,
        bool isImageLike = false)
    {
        Header = header;
        Kind = kind;
        Asset = asset;
        ToolTip = toolTip;
        IsImageLike = isImageLike;
    }

    public string Header { get; }
    public AssetNodeKind Kind { get; }
    public IUnityObjectBase? Asset { get; }
    public string? ToolTip { get; }
    public bool IsImageLike { get; }

    /// <summary>Parent node, set while indexing the tree. Null for roots.</summary>
    public AssetTreeNode? Parent { get; internal set; }

    public ObservableCollection<AssetTreeNode> Children { get; } = new();

    /// <summary>Small leading glyph shown in the tree.</summary>
    public string Glyph => Kind switch
    {
        AssetNodeKind.Bundle => "\U0001F4E6",      // package
        AssetNodeKind.Collection => "\U0001F5C2",  // card index dividers
        AssetNodeKind.TypeGroup => "\U0001F3F7",    // label
        AssetNodeKind.Asset => IsImageLike ? "\U0001F5BC" : "\U0001F4C4", // framed picture / page
        _ => "•",
    };

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value);
    }

    /// <summary>
    /// Recursively applies a name filter (substring, case-insensitive) and a class filter,
    /// returning true if this node stays visible. Null arguments mean "no constraint".
    /// </summary>
    public bool ApplyFilter(string? nameLower, string? className)
    {
        if (Kind == AssetNodeKind.Asset)
        {
            bool nameMatch = nameLower is null || Header.ToLowerInvariant().Contains(nameLower);
            bool classMatch = className is null || string.Equals(Asset?.ClassName, className, StringComparison.Ordinal);
            IsVisible = nameMatch && classMatch;
            return IsVisible;
        }

        bool anyChild = false;
        foreach (AssetTreeNode child in Children)
            anyChild |= child.ApplyFilter(nameLower, className);

        IsVisible = anyChild;
        if (anyChild && (nameLower is not null || className is not null))
            IsExpanded = true;
        return anyChild;
    }
}
