using WeaponLab.Core.Delivery;
using WeaponLab.Core.Game;
using WeaponLab.Core.VData;

namespace WeaponLab.Core.Tests;

/// <summary>The addon's vdata read from its source text (0.22: the source is the addon for green and yellow).</summary>
public sealed class AddonVDataTests
{
    private const string Source = """
        <!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->
        {
            generic_data_type = "CCSWeaponBaseVData"
            rifle =
            {
                _class = "weapon_rifle"
                m_flCycleTime = 0.1
                m_nDamage = 30
            }
            weapon_test =
            {
                _base = "rifle"
                _class = "weapon_test"
                m_nDamage = 40
                m_szWorldModel = resource_name:"models/weapons/test/test.vmdl"
                m_bIsFullAuto = true
                m_flSpread = [ 0.5, 0.6 ]
                m_aShootSounds = { WEAPON_SOUND_SINGLE = soundevent:"Weapon_Test.Single" }
            }
        }
        """;

    [Fact]
    public void SourceValuesResolveThroughTheBaseChain()
    {
        var file = AddonVData.Merge(null, Source);
        var block = file["weapon_test"];
        Assert.NotNull(block);
        Assert.Equal("rifle", block.Base);
        Assert.Equal("40", block.Value("m_nDamage"));
        Assert.Equal("0.1", block.Value("m_flCycleTime")); // From rifle.
        Assert.Equal("models/weapons/test/test.vmdl", block.Value("m_szWorldModel"));
        Assert.Equal("true", block.Value("m_bIsFullAuto"));
        Assert.Equal("0.6", block.Value("m_flSpread[1]"));
        Assert.Equal("Weapon_Test.Single", block.Value("m_aShootSounds.WEAPON_SOUND_SINGLE"));
    }

    /// <summary>
    /// On PC1's test addon, whose source and compiled file were saved together,
    /// the source read on top of the compiled file changes no value. Skipped
    /// where that addon does not exist.
    /// </summary>
    [Fact]
    public void SourceMatchesItsOwnCompileOnTheTestAddon()
    {
        var install = GameLocator.Locate();
        var target = install is null ? null : new AddonTarget("cs_hl_template",
            Path.Combine(install.GamePath, "csgo_addons", "cs_hl_template"), Path.Combine(install.RootPath, "content", "csgo_addons", "cs_hl_template"));
        if (target is null || !File.Exists(target.VDataSourcePath) || !File.Exists(target.VDataCompiledPath) || AddonVData.SourceIsNewer(target))
        {
            Assert.Skip("No cs_hl_template addon with a compiled vdata as new as its source here.");
        }

        var compiled = VDataFile.Read(File.ReadAllBytes(target.VDataCompiledPath), target.VDataCompiledPath);
        var merged = AddonVData.Read(target)!;
        var differences = compiled.Blocks.Where(block => block.IsWeapon)
            .SelectMany(block => block.Properties.Select(property => (block.Name, property.Path, Compiled: property.Value, Source: merged[block.Name]?.Value(property.Path))))
            .Where(item => item.Source is null || !VDataValues.Same(item.Compiled, item.Source))
            .Select(item => $"{item.Name}.{item.Path}: compiled {item.Compiled}, source {item.Source}")
            .ToList();
        Assert.True(differences.Count == 0, string.Join("\n", differences.Take(20)) + $"\n({differences.Count} differences)");
    }
}
