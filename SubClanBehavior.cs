using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace SubClans
{
    public class SubClanBehavior : CampaignBehaviorBase
    {
        private Dictionary<string, CampaignTime> _lastSplitPerClan = new Dictionary<string, CampaignTime>();

        private static int ClanMemberThreshold => SubClansSettings.Instance?.ClanMemberThreshold ?? 10;
        private static float SplitCooldownInDays => SubClansSettings.Instance?.SplitCooldownInDays ?? 15f;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, OnDailyTickClan);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("subclans_last_split", ref _lastSplitPerClan);

            if (_lastSplitPerClan == null)
            {
                _lastSplitPerClan = new Dictionary<string, CampaignTime>();
            }
        }

        private void OnDailyTickClan(Clan clan)
        {
            if (!ShouldSplitClan(clan))
            {
                return;
            }

            SplitClan(clan);
        }

        private bool ShouldSplitClan(Clan clan)
        {
            if (clan == null)
            {
                return false;
            }

            if (clan.IsEliminated || clan.IsBanditFaction || clan.IsRebelClan || clan.IsMinorFaction)
            {
                return false;
            }

            if (clan == Clan.PlayerClan)
            {
                return false;
            }

            if (clan.Leader == null || !clan.Leader.IsAlive || clan.Culture == null)
            {
                return false;
            }

            int nonWandererCount = clan.Heroes?
                .Count(hero => hero != null && hero.Clan == clan && !hero.IsWanderer && hero.IsAlive) ?? 0;
            if (nonWandererCount < ClanMemberThreshold)
            {
                return false;
            }

            if (_lastSplitPerClan.TryGetValue(clan.StringId, out CampaignTime lastSplit))
            {
                float daysSinceSplit = (float)(CampaignTime.Now - lastSplit).ToDays;
                if (daysSinceSplit < SplitCooldownInDays)
                {
                    return false;
                }
            }

            return true;
        }

        private void SplitClan(Clan sourceClan)
        {
            Hero? sourceLeader = sourceClan.Leader;
            List<Hero> familyMembers = CollectDirectFamilyMembers(sourceClan);
            Hero? primaryHeir = DeterminePrimaryHeir(sourceClan);
            Hero? newLeader = SelectNewLeader(sourceClan, familyMembers, primaryHeir);
            if (newLeader == null)
            {
                return;
            }

            CultureObject culture = sourceClan.Culture;
            (TextObject clanName, TextObject informalName) = CreateBranchNames(sourceClan, newLeader);
            string id = $"subclan_{sourceClan.StringId}_{MBRandom.RandomInt(1000000)}";
            Banner? banner = sourceClan.Banner != null ? new Banner(sourceClan.Banner) : null;

            Clan newClan = Clan.CreateClan(id);
            newClan.ChangeClanName(clanName, informalName);
            newClan.Culture = culture;
            if (banner != null)
            {
                newClan.Banner = banner;
            }

            newClan.Color = sourceClan.Color;
            newClan.Color2 = sourceClan.Color2;
            newClan.IsNoble = true;
            newClan.Kingdom = sourceClan.Kingdom;

            Settlement? homeSettlement = ResolveHomeSettlement(sourceClan);
            if (homeSettlement != null)
            {
                newClan.SetInitialHomeSettlement(homeSettlement);
            }

            newLeader.Clan = newClan;
            newClan.SetLeader(newLeader);

            AdjustClanTier(newClan, sourceClan, sourceLeader, newLeader);

            MoveDirectFamily(newLeader, sourceLeader, sourceClan, newClan, familyMembers, primaryHeir);

            CampaignEventDispatcher.Instance.OnClanCreated(newClan, isCompanion: false);
            ShowSplitInformation(sourceClan, newClan, newLeader);
            _lastSplitPerClan[sourceClan.StringId] = CampaignTime.Now;
        }

        private static Hero? SelectNewLeader(Clan sourceClan, List<Hero> familyMembers, Hero? primaryHeir)
        {
            Hero? sourceLeader = sourceClan.Leader;
            foreach (Hero hero in familyMembers)
            {
                if (hero == null || hero == sourceClan.Leader)
                {
                    continue;
                }

                if (!hero.IsAlive || hero.IsPrisoner || hero.IsChild)
                {
                    continue;
                }

                if (hero.Clan != sourceClan)
                {
                    continue;
                }

                if (ReferenceEquals(hero, primaryHeir))
                {
                    continue;
                }

                if (sourceLeader != null && ReferenceEquals(hero, sourceLeader.Spouse))
                {
                    continue;
                }

                return hero;
            }

            return null;
        }

        private static void MoveDirectFamily(Hero newLeader, Hero? originalLeader, Clan sourceClan, Clan newClan, List<Hero> familyMembers, Hero? primaryHeir)
        {
            foreach (Hero member in familyMembers)
            {
                TryMoveFamilyMember(member, newLeader, originalLeader, sourceClan, newClan, primaryHeir);
            }
        }

        private static void TryMoveFamilyMember(Hero? familyMember, Hero newLeader, Hero? originalLeader, Clan sourceClan, Clan newClan, Hero? primaryHeir)
        {
            if (familyMember == null)
            {
                return;
            }

            if (ReferenceEquals(familyMember, newLeader))
            {
                return;
            }

            if (originalLeader != null && ReferenceEquals(familyMember, originalLeader))
            {
                return;
            }

            if (ReferenceEquals(familyMember, primaryHeir))
            {
                return;
            }

            if (!familyMember.IsAlive || familyMember.IsPrisoner || familyMember.Clan != sourceClan)
            {
                return;
            }

            if (originalLeader != null && ReferenceEquals(familyMember, originalLeader.Spouse))
            {
                return;
            }

            familyMember.Clan = newClan;
        }

        private static (TextObject Name, TextObject InformalName) CreateBranchNames(Clan sourceClan, Hero newLeader)
        {
            string suffix = SubClansSettings.Instance?.BranchSuffix ?? "Cadet Branch";

            TextObject formal = new TextObject("{=SubClans_BranchFormal}{LEADER}'s {SOURCE_CLAN} {SUFFIX}");
            formal.SetTextVariable("LEADER", newLeader.Name);
            formal.SetTextVariable("SOURCE_CLAN", sourceClan.Name);
            formal.SetTextVariable("SUFFIX", suffix);

            TextObject informal = new TextObject("{=SubClans_BranchInformal}{LEADER} {SUFFIX}");
            informal.SetTextVariable("LEADER", newLeader.Name);
            informal.SetTextVariable("SUFFIX", suffix);

            return (formal, informal);
        }

        private static void ShowSplitInformation(Clan sourceClan, Clan newClan, Hero newLeader)
        {
            TextObject message = new TextObject("{=SubClans_SplitInfo}{SOURCE_CLAN} has founded {NEW_CLAN} under {LEADER}.");
            message.SetTextVariable("SOURCE_CLAN", sourceClan.Name);
            message.SetTextVariable("NEW_CLAN", newClan.Name);
            message.SetTextVariable("LEADER", newLeader.Name);
            InformationMessage globalMessage = new InformationMessage(message.ToString());
            InformationManager.DisplayMessage(globalMessage);

            if (ShouldNotifyPlayer(sourceClan, newClan))
            {
                TextObject personal = new TextObject("{=SubClans_PlayerInfo}Your clan has reorganized: {SOURCE_CLAN} created {NEW_CLAN} led by {LEADER}.");
                personal.SetTextVariable("SOURCE_CLAN", sourceClan.Name);
                personal.SetTextVariable("NEW_CLAN", newClan.Name);
                personal.SetTextVariable("LEADER", newLeader.Name);

                InformationMessage playerMessage = new InformationMessage(personal.ToString(), Color.FromUint(0xFFE17D00));
                InformationManager.DisplayMessage(playerMessage);
            }
        }

        private static bool ShouldNotifyPlayer(Clan sourceClan, Clan newClan)
        {
            Clan playerClan = Clan.PlayerClan;
            if (playerClan == null)
            {
                return false;
            }

            if (ReferenceEquals(sourceClan, playerClan) || ReferenceEquals(newClan, playerClan))
            {
                return true;
            }

            Kingdom? playerKingdom = playerClan.Kingdom;
            if (playerKingdom != null && (ReferenceEquals(sourceClan.Kingdom, playerKingdom) || ReferenceEquals(newClan.Kingdom, playerKingdom)))
            {
                return true;
            }

            return false;
        }

        private static List<Hero> CollectDirectFamilyMembers(Clan clan)
        {
            var orderedMembers = new List<Hero>();
            var seen = new HashSet<Hero>();
            Hero? leader = clan.Leader;
            if (leader == null)
            {
                return orderedMembers;
            }

            AddIfEligible(orderedMembers, seen, leader, clan);
            AddIfEligible(orderedMembers, seen, leader.Spouse, clan);
            AddIfEligible(orderedMembers, seen, leader.Father, clan);
            AddIfEligible(orderedMembers, seen, leader.Mother, clan);

            foreach (Hero child in leader.Children)
            {
                AddIfEligible(orderedMembers, seen, child, clan);
            }

            foreach (Hero sibling in leader.Siblings)
            {
                AddIfEligible(orderedMembers, seen, sibling, clan);
            }

            return orderedMembers;
        }

        private static Hero? DeterminePrimaryHeir(Clan clan)
        {
            Hero? leader = clan.Leader;
            if (leader == null || Campaign.Current?.Models?.AgeModel == null)
            {
                return null;
            }

            int adultAge = Campaign.Current.Models.AgeModel.HeroComesOfAge;

            IEnumerable<Hero> eligibleChildren = leader.Children
                .Where(child => child != null && child.Clan == clan && child.IsAlive && !child.IsWanderer && child.Age >= adultAge);

            Hero? oldestSon = eligibleChildren
                .Where(child => !child.IsFemale)
                .OrderByDescending(child => child.Age)
                .FirstOrDefault();

            if (oldestSon != null)
            {
                return oldestSon;
            }

            return eligibleChildren
                .OrderByDescending(child => child.Age)
                .FirstOrDefault();
        }

        private static void AddIfEligible(ICollection<Hero> orderedMembers, ISet<Hero> seen, Hero? hero, Clan clan)
        {
            if (hero == null)
            {
                return;
            }

            if (!hero.IsAlive || hero.Clan != clan || hero.IsWanderer)
            {
                return;
            }

            if (seen.Add(hero))
            {
                orderedMembers.Add(hero);
            }
        }

        private static void AdjustClanTier(Clan newClan, Clan sourceClan, Hero? originalLeader, Hero newLeader)
        {
            ClanTierModel? tierModel = Campaign.Current?.Models?.ClanTierModel;
            if (tierModel == null)
            {
                return;
            }

            bool isDirectDescendant = originalLeader != null &&
                (ReferenceEquals(newLeader.Father, originalLeader) || ReferenceEquals(newLeader.Mother, originalLeader));

            int targetTier = isDirectDescendant ? sourceClan.Tier : 2;
            targetTier = Math.Max(targetTier, tierModel.MinClanTier);
            targetTier = Math.Min(targetTier, tierModel.MaxClanTier);

            if (targetTier <= newClan.Tier)
            {
                return;
            }

            float requiredRenown = tierModel.GetRequiredRenownForTier(targetTier);
            float deltaRenown = requiredRenown - newClan.Renown;

            if (deltaRenown > 0f)
            {
                newClan.AddRenown(deltaRenown);
            }
        }

        private static Settlement? ResolveHomeSettlement(Clan sourceClan)
        {
            if (sourceClan.HomeSettlement != null)
            {
                return sourceClan.HomeSettlement;
            }

            if (sourceClan.Leader?.HomeSettlement != null)
            {
                return sourceClan.Leader.HomeSettlement;
            }

            if (sourceClan.Leader?.CurrentSettlement != null)
            {
                return sourceClan.Leader.CurrentSettlement;
            }

            foreach (Settlement settlement in sourceClan.Settlements)
            {
                if (settlement != null)
                {
                    return settlement;
                }
            }

            return null;
        }
    }
}
