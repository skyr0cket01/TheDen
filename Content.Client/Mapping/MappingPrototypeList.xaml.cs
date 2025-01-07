﻿using System.Numerics;
using Robust.Client.AutoGenerated;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Prototypes;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client.Mapping;

[GenerateTypedNameReferences]
public sealed partial class MappingPrototypeList : Control
{
    private (int start, int end) _lastIndices;
    private readonly List<MappingPrototype> _allPrototypes = new();
    private readonly List<Texture> _insertTextures = new();
    private readonly List<MappingPrototype> _search = new();

    public MappingSpawnButton? Selected;

    /// <summary>
    /// if true, elements with no children will be grouped into a grid
    /// </summary>
    public bool Gallery { get; set; }

    public Color? TexturesModulate { get; set; }

    public Action<IPrototype, List<Texture>>? GetPrototypeData;
    public event Action<MappingPrototypeList, MappingSpawnButton, IPrototype?>? SelectionChanged;

    public MappingPrototypeList()
    {
        RobustXamlLoader.Load(this);

        MeasureButton.Measure(Vector2Helpers.Infinity);

        ScrollContainer.OnScrolled += UpdateSearch;
        CollapseAllButton.OnPressed += OnCollapseAll;
        SearchBar.OnTextChanged += OnSearch;
        ClearSearchButton.OnPressed += _ =>
        {
            SearchBar.Text = string.Empty;
            OnSearch(new LineEdit.LineEditEventArgs(SearchBar, string.Empty));
        };
        OnResized += UpdateSearch;

        CollapseAllButton.Texture.TexturePath = "/Textures/Interface/VerbIcons/collapse.svg.192dpi.png";
        ClearSearchButton.Texture.TexturePath = "/Textures/Interface/VerbIcons/xmark-solid.svg.192dpi.png";
    }

    public void UpdateVisible(MappingPrototype prototype, List<MappingPrototype> allPrototypes)
    {
        _allPrototypes.Clear();
        PrototypeList.DisposeAllChildren();
        _allPrototypes.AddRange(allPrototypes);

        Selected = null;
        ScrollContainer.SetScrollValue(new Vector2(0, 0));

        Insert(PrototypeList, prototype, true, Gallery);
    }

    private MappingSpawnButton Insert(Container list, MappingPrototype mapping, bool includeChildren, bool galleryLayout)
    {
        var prototype = mapping.Prototype;

        _insertTextures.Clear();

        if (prototype != null)
            GetPrototypeData?.Invoke(prototype, _insertTextures);

        var button = new MappingSpawnButton { Prototype = mapping };
        button.Label.Text = mapping.Name;

        if (_insertTextures.Count > 0)
            button.SetTextures(_insertTextures);

        if (TexturesModulate is { } modulate)
            button.Texture.Modulate = modulate;

        if (prototype != null && button.Prototype == Selected?.Prototype)
        {
            Selected = button;
            button.Button.Pressed = true;
        }

        list.AddChild(button);

        button.Button.OnToggled += _ => SelectionChanged?.Invoke(this, button, prototype);

        if (includeChildren && mapping.Children?.Count > 0)
        {
            button.CollapseButton.Visible = true;
            button.CollapseButton.OnToggled += _ => ToggleCollapse(button);
        }
        else
        {
            if (galleryLayout)
                button.Gallery();
            button.CollapseButtonWrapper.Visible = false;
            button.CollapseButton.Visible = false;
        }

        return button;
    }

    private void Search(List<MappingPrototype> prototypes)
    {
        _search.Clear();
        SearchList.DisposeAllChildren();
        _lastIndices = (0, -1);

        _search.AddRange(prototypes);
        SearchList.TotalItemCount = _search.Count;
        ScrollContainer.SetScrollValue(new Vector2(0, 0));

        UpdateSearch();
    }

    /// <summary>
    ///     Constructs a virtual list where not all buttons exist at one time, since there may be thousands of them.
    /// </summary>
    private void UpdateSearch()
    {
        if (!SearchList.Visible)
            return;

        var height = MeasureButton.DesiredSize.Y + PrototypeListContainer.Separation;
        var offset = Math.Max(-SearchList.Position.Y, 0);
        var startIndex = (int) Math.Floor(offset / height);
        SearchList.ItemOffset = startIndex;

        var (prevStart, prevEnd) = _lastIndices;
        var endIndex = startIndex - 1;
        var spaceUsed = -height;

        // calculate how far down we are scrolled
        while (spaceUsed < SearchList.Parent!.Height)
        {
            spaceUsed += height;
            endIndex += 1;
        }

        endIndex = Math.Min(endIndex, _search.Count - 1);

        // nothing changed in terms of which buttons are visible now and before
        if (endIndex == prevEnd && startIndex == prevStart)
            return;

        _lastIndices = (startIndex, endIndex);

        // remove previously seen but now unseen buttons from the top
        for (var i = prevStart; i < startIndex && i <= prevEnd; i++)
        {
            var control = SearchList.GetChild(0);
            SearchList.RemoveChild(control);
        }

        // remove previously seen but now unseen buttons from the bottom
        for (var i = prevEnd; i > endIndex && i >= prevStart; i--)
        {
            var control = SearchList.GetChild(SearchList.ChildCount - 1);
            SearchList.RemoveChild(control);
        }

        // insert buttons that can now be seen, from the start
        for (var i = Math.Min(prevStart - 1, endIndex); i >= startIndex; i--)
        {
            Insert(SearchList, _search[i], false, false).SetPositionInParent(0);
        }

        // insert buttons that can now be seen, from the end
        for (var i = Math.Max(prevEnd + 1, startIndex); i <= endIndex; i++)
        {
            Insert(SearchList, _search[i], false, false);
        }
    }

    private void OnCollapseAll(ButtonEventArgs args)
    {
        foreach (var child in PrototypeList.Children)
        {
            if (child is not MappingSpawnButton button)
                continue;

            button.Collapse();
        }

        ScrollContainer.SetScrollValue(new Vector2(0, 0));
    }

    public void ToggleCollapse(MappingSpawnButton button)
    {
        if (!button.CollapseButton.Pressed)
        {
            button.Collapse();
            return;
        }

        button.UnCollapse();
        if (button.Prototype?.Children == null)
            return;

        foreach (var child in button.Prototype.Children)
        {
            if (child.Children == null && Gallery)
                Insert(button.ChildrenPrototypesGallery, child, false, true);
            else
                Insert(button.ChildrenPrototypes, child, true, false);
        }
    }

    private void OnSearch(LineEdit.LineEditEventArgs args)
    {
        if (string.IsNullOrEmpty(args.Text))
        {
            PrototypeList.Visible = true;
            SearchList.Visible = false;
            return;
        }

        var matches = new List<MappingPrototype>();
        foreach (var prototype in _allPrototypes)
        {
            if (prototype.Name.Contains(args.Text, StringComparison.OrdinalIgnoreCase))
                matches.Add(prototype);
        }

        matches.Sort(static (a, b) =>
            string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        PrototypeList.Visible = false;
        SearchList.Visible = true;
        Search(matches);
    }
}
