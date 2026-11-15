using ValveKeyValue;
using WeaponLab.Core.Clips;

namespace WeaponLab.Core.Tests;

public sealed class ClipReaderTests
{
    [Fact]
    public void EventsAreReadByClass()
    {
        var events = KVObject.Array();
        events.Add(Event("CNmIDEvent", 0.25, ("m_ID", "WPN_DROP_MAG"), ("m_secondaryID", "clip")));
        events.Add(Event("CNmSoundEvent", 0.5, ("m_name", "Weapon_AK47.Clipin")));
        events.Add(Event("CNmFootEvent", 1.0, ("m_syncID", "left")));
        var clipData = KVObject.Collection();
        clipData.Add("m_events", events);

        var result = ClipReader.ReadEvents(clipData);

        Assert.Equal(3, result.Count);
        Assert.Equal(new ClipEvent(0, ClipEventKind.Gameplay, "CNmIDEvent", "WPN_DROP_MAG", "clip", 0.25, 0.1), result[0] with { Fields = EventFields.Empty });
        Assert.Equal(new ClipEvent(1, ClipEventKind.Sound, "CNmSoundEvent", "Weapon_AK47.Clipin", "", 0.5, 0.1), result[1] with { Fields = EventFields.Empty });
        Assert.Equal("clip", result[0].Fields["m_secondaryID"]); // Plain values are kept as fields too.
        Assert.Equal(ClipEventKind.Other, result[2].Kind);
        Assert.Equal("left", result[2].Name);
    }

    [Fact]
    public void MissingEventArrayGivesNoEvents()
    {
        Assert.Empty(ClipReader.ReadEvents(KVObject.Collection()));
    }

    [Fact]
    public void EventTimesConvertToFramesAndSeconds()
    {
        // reload_ak: 74 frames over 2.4333 s; its Clipout sound sits on frame 11.
        var clip = new ClipInfo("animation/anims/viewmodel/rifle/rifle_ak/reload_ak.vnmclip_c", "", 74, 73 / 30.0, false, ClipInfo.ViewmodelSkeleton, ["ak47"], 56, []);
        var clipout = new ClipEvent(2, ClipEventKind.Sound, "CNmSoundEvent", "Weapon_AK47.Clipout", "", 11 / 73.0, 0);

        Assert.Equal("reload_ak", clip.Name);
        Assert.Equal(30, clip.FramesPerSecond, precision: 6);
        Assert.Equal(11, clipout.StartFrame(clip), precision: 6);
        Assert.Equal(11 / 30.0, clipout.StartSeconds(clip), precision: 6);
        Assert.True(clip.IsEditable);
    }

    private static KVObject Event(string className, double start, params (string Key, string Value)[] fields)
    {
        var value = KVObject.Collection();
        value.Add("_class", className);
        value.Add("m_flStartTime", Wrapped(start));
        value.Add("m_flDuration", Wrapped(0.1));
        foreach (var (key, text) in fields)
        {
            value.Add(key, text);
        }

        return value;
    }

    private static KVObject Wrapped(double number)
    {
        var wrapper = KVObject.Collection();
        wrapper.Add("m_flValue", number);
        return wrapper;
    }
}
