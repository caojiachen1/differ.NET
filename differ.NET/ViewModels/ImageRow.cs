using System.Collections.Generic;
using differ.NET.Models;

namespace differ.NET.ViewModels;

public sealed class ImageRow
{
    public ImageRow(int index, IReadOnlyList<ImageItem> items)
    {
        Index = index;
        Items = items;
    }

    public int Index { get; }

    public IReadOnlyList<ImageItem> Items { get; }
}
