# SubClans

## Overview
SubClans is a Bannerlord campaign behavior that keeps noble families manageable by creating culturally themed cadet branches whenever a clan becomes too large. The mod monitors every non-bandit clan during the daily tick, tracks their recent split history, and dynamically spins off descendants into new, lore-friendly sub-clans that inherit culture, colors, banners, and political connections.

## Key Features
- **Automated cadet branches**: When a clan has ten or more living, non-wanderer members, a cadet branch can form to keep the roster balanced.
- **Complete family unit transfers**: When a branch forms, the new leader's entire family subtree is transferred—including all spouses (current and ex-), children, grandchildren, great-grandchildren, and their respective spouses—keeping family units coherent.
- **Native family data integration**: Uses Bannerlord's built-in `Spouse`, `ExSpouses`, and `Children` properties to discover the full family graph, including deceased relatives whose lineage records are preserved in the new clan.
- **Recursive descendant collection**: Descendants are collected to arbitrary depth (children, grandchildren, etc.) rather than only one generation, ensuring deep family trees split correctly.
- **Respect for succession**: The oldest adult child (preferring sons) remains in the parent clan as the primary heir. All spouses of the current leader are protected from reassignment.
- **Cultural flavor**: New clans inherit their parent’s culture, banner colors, and a cadet-branch name that references both the parent clan and the new leader.
- **Tier-aware start**: Direct descendants inherit the parent clan’s tier; other branch leaders start at tier 2 and are automatically granted enough renown to reach it.
- **Persistent cooldowns**: Each clan tracks the last time it split, preventing repeated branches within 15 in-game days—even across save/load cycles.
- **Player visibility**: All splits are broadcast through world messages, and the player receives a highlighted notification if their clan or kingdom is involved.
- **Graceful fallback**: If `ExSpouses` is unavailable (e.g., older game versions), the mod falls back to the single `Spouse` property automatically.
- **Downward-only transfers**: Only the new leader's spouses and descendants move to the cadet branch — parents and older-generation relatives always remain in the source clan.

## How It Works
1. **Event hook**: The mod registers a listener to `CampaignEvents.DailyTickClanEvent` during campaign start.
2. **Eligibility check**: The listener performs fast sanity checks (eliminated factions, bandits, rebels, minors, player clan) and then counts living, non-wanderer heroes.
3. **Candidate collection**: The source clan leader's full family is enumerated—using `Spouse`, `ExSpouses`, `Father`, `Mother`, recursive `Children`, and `Siblings`—to build a prioritized list of new leader candidates. Only living, non-wanderer clan members qualify as candidates.
4. **Leadership selection**: A new leader is chosen from eligible family members, excluding the current leader, heirs, all spouses of the current leader, prisoners, children, and the deceased.
5. **Family unit collection**: Before any clan assignments change, the new leader's complete family subtree is collected via `CollectFamilyUnit`. This includes all their spouses (current + ex-), all descendants recursively, each descendant's spouses, and deceased relatives.
6. **Clan creation**: A fresh clan is instantiated via `Clan.CreateClan`, the name and banner are applied, and the settlement affinity mirrors the parent clan.
7. **Family transfer**: The collected family unit is transferred to the cadet branch. The original leader, their spouses, the primary heir, wanderers, and living prisoners remain in the source clan. Deceased family members of the new leader are reassigned to preserve lineage continuity.
8. **Tier balancing**: Renown adjustments ensure the branch meets the tier target determined by lineage.
9. **Notifications**: Campaign events and information messages inform the player base.

## Installation
1. Build the mod with `dotnet build SubClans.csproj` (both net472 and net6 binaries are produced).
2. Copy the compiled assemblies and `_Module` folder into a new Bannerlord module directory (for example, `Mount & Blade II Bannerlord\Modules\SubClans`).
3. Ensure the module is enabled in the Bannerlord launcher, loading after dependencies such as Native, SandBox, StoryMode, and CustomBattle.
4. Launch a campaign or continue an existing save. The behavior activates automatically on load.

## Configuration
- **Clan size threshold**: Hardcoded to 10 members in `SubClanBehavior`. Adjust `ClanMemberThreshold` to change the trigger.
- **Split cooldown**: Hardcoded to 15 in-game days. Modify `SplitCooldownInDays` to tune frequency.
- **Tier defaults**: Direct descendants inherit the parent tier; unrelated leaders start at tier 2. These rules are managed inside `AdjustClanTier`.

## Compatibility Notes
- Designed for singleplayer campaigns using TaleWorlds 1.2.x+ assemblies.
- Works alongside Diplomacy and similar political overhauls. Increased sub-clan diversity can lead to more organic civil wars.
- Save-game friendly: the behavior can be added to existing campaigns, though already inflated clans will begin splitting after the cooldown expires.
- No configuration UI is provided; edits require recompiling the mod.
- **Native family data**: The mod relies on `Hero.Spouse`, `Hero.ExSpouses`, and `Hero.Children` to discover family relationships. If `ExSpouses` is unavailable at runtime (older game versions or stripped assemblies), the mod falls back to checking only the primary `Spouse` property.
- **Custom marriage systems**: Mods that implement fully custom marriage systems (bypassing `Hero.Spouse`/`ExSpouses` entirely) are **not directly supported**. Compatibility patches or bridge mods are recommended for those setups. As long as a marriage mod populates the native `Spouse` and/or `ExSpouses` fields, SubClans will pick up those relationships automatically.

## Family Selection Logic
The following outlines which heroes are transferred when a cadet branch forms:

### Leader Candidate Pool (`CollectDirectFamilyMembers`)
- Source leader + all their spouses (current & ex-)
- Source leader's parents
- All descendants of the source leader (recursively: children, grandchildren, …) and each descendant's spouses
- Source leader's siblings
- **Filter**: Only living, non-wanderer heroes belonging to the source clan qualify as candidates

### New Leader Exclusions (`SelectNewLeader`)
The following heroes are never chosen as a branch leader:
- The current clan leader
- The primary heir (oldest eligible adult child, preferring sons)
- All spouses of the current leader (current + ex-)
- Deceased, imprisoned, or underage heroes

### Family Unit for Transfer (`CollectFamilyUnit`)
Once a leader is selected, their complete family subtree is collected:
- The new leader themselves
- All of the new leader's spouses (current + ex-)
- All descendants recursively, plus each descendant's spouses
- **Deceased heroes are included** to preserve lineage records in the new clan

### Transfer Protections (`TryMoveFamilyMember`)
The following heroes are never transferred out of the source clan:
- The original clan leader
- The primary heir
- All spouses of the original leader
- Living prisoners
- Wanderers
- Heroes already belonging to a different clan

### Limitations
- Only heroes whose `.Clan` matches the source clan are eligible for collection or transfer. Spouses or descendants who have already joined another clan are not affected.
- Bannerlord's `ExSpouses` list may not reflect every relationship created by non-standard marriage mods. If a mod does not populate `ExSpouses`, those spouses will not be discovered.
- The mod does not create marriage relationships—it only reads existing native data.

## Development
- Target frameworks: .NET Framework 4.7.2 and .NET 6.
- Required references are resolved via the Bannerlord installation paths defined in `SubClans.csproj`.
- The project uses nullable reference types and C# 10 features enabled through `<LangVersion>10.0</LangVersion>`.

### Building
```bash
cd SubClans
 dotnet build SubClans.csproj
```
Binaries are emitted to `bin/Debug/net472` and `bin/Debug/net6`. Release builds can be produced with `dotnet build -c Release SubClans.csproj`.

## Credits
- Original concept and testing: Dolobarsch.
- Implementation: Dolobarsch.
- Bannerlord and TaleWorlds assemblies remain the property of TaleWorlds Entertainment.
