using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;

namespace SubClans
{
    internal sealed class SubClansSettings : AttributeGlobalSettings<SubClansSettings>
    {
        public override string Id => "SubClans";
        public override string DisplayName => "Sub Clans";
        public override string FolderName => "SubClans";
        public override string FormatType => "json2";

        [SettingPropertyText("{=SubClans_Setting_BranchSuffix}Branch Suffix", Order = 0, RequireRestart = false,
            HintText = "{=SubClans_Setting_BranchSuffix_Hint}The suffix appended to the clan name, e.g. 'Cadet Branch' produces 'Leader's SourceClan Cadet Branch'.")]
        [SettingPropertyGroup("{=SubClans_Setting_Group_Naming}Naming")]
        public string BranchSuffix { get; set; } = "Cadet Branch";

        [SettingPropertyInteger("{=SubClans_Setting_Threshold}Clan Member Threshold", 2, 30, Order = 0, RequireRestart = false,
            HintText = "{=SubClans_Setting_Threshold_Hint}Minimum number of non-wanderer living heroes in a clan before it can split.")]
        [SettingPropertyGroup("{=SubClans_Setting_Group_Split}Split Rules")]
        public int ClanMemberThreshold { get; set; } = 10;

        [SettingPropertyFloatingInteger("{=SubClans_Setting_Cooldown}Split Cooldown (Days)", 1f, 365f, "#0", Order = 1, RequireRestart = false,
            HintText = "{=SubClans_Setting_Cooldown_Hint}Minimum number of days between splits for the same clan.")]
        [SettingPropertyGroup("{=SubClans_Setting_Group_Split}Split Rules")]
        public float SplitCooldownInDays { get; set; } = 15f;
    }
}
