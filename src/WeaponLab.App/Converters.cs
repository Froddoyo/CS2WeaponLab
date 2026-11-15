using Avalonia.Data.Converters;
using Avalonia.Media;
using WeaponLab.Core.Profiles;

namespace WeaponLab.App;

public static class Converters
{
    private static readonly IBrush NeedsApply = new SolidColorBrush(Color.Parse("#E3C14F"));
    private static readonly IBrush Applied = new SolidColorBrush(Color.Parse("#6BCB77"));
    private static readonly IBrush Elsewhere = new SolidColorBrush(Color.Parse("#7FB8FF"));
    private static readonly IBrush PendingElsewhere = new SolidColorBrush(Color.Parse("#4FD1C5"));
    private static readonly IBrush Normal = Brushes.Gainsboro;

    public static readonly IValueConverter BoldWhenTrue =
        new FuncValueConverter<bool, FontWeight>(value => value ? FontWeight.SemiBold : FontWeight.Normal);

    public static readonly IValueConverter WarningBrush =
        new FuncValueConverter<bool, IBrush>(value => value ? new SolidColorBrush(Color.Parse("#E8A060")) : Brushes.Gainsboro);

    /// <summary>Dot colour: yellow = profile edits not in the addon, green = addon matches.</summary>
    public static readonly IValueConverter StateBrush =
        new FuncValueConverter<WeaponState, IBrush>(state => state switch
        {
            WeaponState.NeedsApply => NeedsApply,
            WeaponState.Applied => Applied,
            WeaponState.PendingElsewhere => PendingElsewhere,
            WeaponState.ChangedElsewhere => Elsewhere,
            _ => Brushes.Transparent,
        });

    public static readonly IValueConverter StateTextBrush =
        new FuncValueConverter<WeaponState, IBrush>(state => state switch
        {
            WeaponState.NeedsApply => NeedsApply,
            WeaponState.Applied => Applied,
            WeaponState.PendingElsewhere => PendingElsewhere,
            WeaponState.ChangedElsewhere => Elsewhere,
            _ => Normal,
        });
}
