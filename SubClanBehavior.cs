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

            // Collect the new leader's complete family unit before changing clan assignments.
            // Uses native family data (Spouse, ExSpouses, Children) to capture all spouses,
            // descendants, and deceased relatives for a coherent family transfer.
            List<Hero> newLeaderFamily = CollectFamilyUnit(newLeader, sourceClan);

            // Protect all spouses of the original leader from being transferred.
            HashSet<Hero> protectedSpouses = sourceLeader != null
                ? new HashSet<Hero>(GetAllSpouses(sourceLeader))
                : new HashSet<Hero>();

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
            if (homeSettlement == null)
            {
                // A clan without a home settlement will crash the kingdom election system
                // (GeographicalAdvantageForFaction). Abort the split to prevent this.
                return;
            }

            newClan.SetInitialHomeSettlement(homeSettlement);

            newLeader.Clan = newClan;
            newClan.SetLeader(newLeader);

            AdjustClanTier(newClan, sourceClan, sourceLeader, newLeader);

            MoveDirectFamily(newLeader, sourceLeader, sourceClan, newClan, newLeaderFamily, primaryHeir, protectedSpouses);

            CampaignEventDispatcher.Instance.OnClanCreated(newClan, isCompanion: false);
            ShowSplitInformation(sourceClan, newClan, newLeader);
            _lastSplitPerClan[sourceClan.StringId] = CampaignTime.Now;
        }

        private static Hero? SelectNewLeader(Clan sourceClan, List<Hero> familyMembers, Hero? primaryHeir)
        {
            Hero? sourceLeader = sourceClan.Leader;

            // Protect all spouses of the source leader (current + ex-spouses)
            // from being selected as a new branch leader.
            HashSet<Hero> leaderSpouses = sourceLeader != null
                ? new HashSet<Hero>(GetAllSpouses(sourceLeader))
                : new HashSet<Hero>();

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

                if (leaderSpouses.Contains(hero))
                {
                    continue;
                }

                return hero;
            }

            return null;
        }

        private static void MoveDirectFamily(Hero newLeader, Hero? originalLeader, Clan sourceClan, Clan newClan, List<Hero> familyMembers, Hero? primaryHeir, HashSet<Hero> protectedSpouses)
        {
            foreach (Hero member in familyMembers)
            {
                TryMoveFamilyMember(member, newLeader, originalLeader, sourceClan, newClan, primaryHeir, protectedSpouses);
            }
        }

        /// <summary>
        /// Attempts to transfer a single family member to the new cadet branch clan.
        /// Deceased family members are transferred to maintain complete lineage records.
        /// Living prisoners are not transferred. All spouses of the original leader are protected.
        /// </summary>
        private static void TryMoveFamilyMember(Hero? familyMember, Hero newLeader, Hero? originalLeader, Clan sourceClan, Clan newClan, Hero? primaryHeir, HashSet<Hero> protectedSpouses)
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

            if (familyMember.Clan != sourceClan)
            {
                return;
            }

            // Living prisoners should not be transferred.
            if (familyMember.IsAlive && familyMember.IsPrisoner)
            {
                return;
            }

            // Protect all spouses of the original leader (current + ex-spouses).
            if (protectedSpouses.Contains(familyMember))
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

        /// <summary>
        /// Collects all direct family members of the clan leader for new leader candidate selection.
        /// Uses native family data (Spouse, ExSpouses, Children) to build a complete picture
        /// of the leader's family, including all spouses and recursive descendants.
        /// Only living, non-wanderer clan members are included for leader selection purposes.
        /// </summary>
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

            // Add all spouses using native family data (current + ex-spouses).
            // Falls back to Spouse-only if ExSpouses is unavailable.
            foreach (Hero spouse in GetAllSpouses(leader))
            {
                AddIfEligible(orderedMembers, seen, spouse, clan);
            }

            AddIfEligible(orderedMembers, seen, leader.Father, clan);
            AddIfEligible(orderedMembers, seen, leader.Mother, clan);

            // Recursively collect all descendants (children, grandchildren, etc.)
            // and their spouses for comprehensive candidate selection.
            CollectDescendantsRecursive(orderedMembers, seen, leader, clan, includeDeceased: false);

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

        /// <summary>
        /// Adds a hero to the family collection including deceased heroes.
        /// Used for complete family unit transfers that preserve lineage data.
        /// Deceased family members are included to maintain coherent family records
        /// in the new cadet branch clan.
        /// </summary>
        private static void AddFamilyMember(ICollection<Hero> members, ISet<Hero> seen, Hero? hero, Clan clan)
        {
            if (hero == null || hero.IsWanderer)
            {
                return;
            }

            if (hero.Clan != clan)
            {
                return;
            }

            if (seen.Add(hero))
            {
                members.Add(hero);
            }
        }

        /// <summary>
        /// Returns all spouses of a hero using Bannerlord's native family data.
        /// Includes the current Spouse and all ExSpouses (divorced/deceased spouses).
        /// Falls back to only the primary Spouse if ExSpouses is unavailable,
        /// maintaining compatibility with older game versions.
        /// Note: Fully custom marriage systems may require separate compatibility mods.
        /// </summary>
        private static List<Hero> GetAllSpouses(Hero hero)
        {
            var spouses = new List<Hero>();

            if (hero.Spouse != null)
            {
                spouses.Add(hero.Spouse);
            }

            try
            {
                var exSpouses = hero.ExSpouses;
                if (exSpouses != null)
                {
                    foreach (Hero ex in exSpouses)
                    {
                        if (ex != null && !spouses.Contains(ex))
                        {
                            spouses.Add(ex);
                        }
                    }
                }
            }
            catch
            {
                // ExSpouses property not available in this game version.
                // Falling back to Spouse-only check (already handled above).
            }

            return spouses;
        }

        /// <summary>
        /// Recursively collects all descendants (children, grandchildren, great-grandchildren, etc.)
        /// of a hero, along with each descendant's spouses. When includeDeceased is true,
        /// deceased family members are also collected to preserve complete lineage records.
        /// </summary>
        private static void CollectDescendantsRecursive(ICollection<Hero> members, ISet<Hero> seen, Hero ancestor, Clan clan, bool includeDeceased)
        {
            if (ancestor.Children == null)
            {
                return;
            }

            foreach (Hero child in ancestor.Children)
            {
                if (child == null)
                {
                    continue;
                }

                if (includeDeceased)
                {
                    AddFamilyMember(members, seen, child, clan);
                }
                else
                {
                    AddIfEligible(members, seen, child, clan);
                }

                // Collect all spouses of this descendant.
                foreach (Hero spouse in GetAllSpouses(child))
                {
                    if (includeDeceased)
                    {
                        AddFamilyMember(members, seen, spouse, clan);
                    }
                    else
                    {
                        AddIfEligible(members, seen, spouse, clan);
                    }
                }

                // Recurse to collect grandchildren and beyond.
                CollectDescendantsRecursive(members, seen, child, clan, includeDeceased);
            }
        }

        /// <summary>
        /// Collects the complete family unit of the designated new leader for cadet branch transfer.
        /// Includes all spouses (current and ex-), all descendants recursively (children,
        /// grandchildren, etc.), and their respective spouses. Deceased family members
        /// are included to maintain complete lineage records in the new clan.
        /// Falls back to direct-family-only collection if native family data structures
        /// (e.g., ExSpouses) are unavailable.
        /// </summary>
        private static List<Hero> CollectFamilyUnit(Hero leader, Clan sourceClan)
        {
            var familyUnit = new List<Hero>();
            var seen = new HashSet<Hero>();

            // Add the leader themselves.
            AddFamilyMember(familyUnit, seen, leader, sourceClan);

            // Add all spouses (Spouse + ExSpouses) using native family data.
            foreach (Hero spouse in GetAllSpouses(leader))
            {
                AddFamilyMember(familyUnit, seen, spouse, sourceClan);
            }

            // Recursively collect all descendants and their spouses (including deceased).
            CollectDescendantsRecursive(familyUnit, seen, leader, sourceClan, includeDeceased: true);

            return familyUnit;
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

            // Last resort: find any town in the kingdom so the clan has a valid
            // home settlement reference. Without one, the kingdom election system
            // crashes in GeographicalAdvantageForFaction.
            Kingdom? kingdom = sourceClan.Kingdom;
            if (kingdom != null)
            {
                foreach (Settlement settlement in kingdom.Settlements)
                {
                    if (settlement != null && settlement.IsTown)
                    {
                        return settlement;
                    }
                }

                foreach (Settlement settlement in kingdom.Settlements)
                {
                    if (settlement != null)
                    {
                        return settlement;
                    }
                }
            }

            return null;
        }
    }
}
