# SubClans

## Overview
SubClans is a Bannerlord campaign behavior that keeps noble families manageable by creating culturally themed cadet branches whenever a clan becomes too large. The mod monitors every non-bandit clan during the daily tick, tracks their recent split history, and dynamically spins off descendants into new, lore-friendly sub-clans that inherit culture, colors, banners, and political connections.

## Key Features
- **Automated cadet branches**: When a clan has ten or more living, non-wanderer members, a cadet branch can form to keep the roster balanced.
- **Family-aware splits**: Only direct relatives of the ruling family are considered. Dead heroes, prisoners, wanderers, and the sitting clan leader are excluded from the split.
- **Respect for succession**: The oldest adult child (preferring sons) remains in the parent clan as the primary heir. The current leader’s spouse is never reassigned to the new clan.
- **Cultural flavor**: New clans inherit their parent’s culture, banner colors, and a cadet-branch name that references both the parent clan and the new leader.
- **Tier-aware start**: Direct descendants inherit the parent clan’s tier; other branch leaders start at tier 2 and are automatically granted enough renown to reach it.
- **Persistent cooldowns**: Each clan tracks the last time it split, preventing repeated branches within 15 in-game days—even across save/load cycles.
- **Player visibility**: All splits are broadcast through world messages, and the player receives a highlighted notification if their clan or kingdom is involved.

## How It Works
1. **Event hook**: The mod registers a listener to `CampaignEvents.DailyTickClanEvent` during campaign start.
2. **Eligibility check**: The listener performs fast sanity checks (eliminated factions, bandits, rebels, minors, player clan) and then counts living, non-wanderer heroes.
3. **Leadership selection**: A new leader is chosen from eligible family members, excluding the current leader, heirs, spouses, prisoners, children, and the deceased.
4. **Clan creation**: A fresh clan is instantiated via `Clan.CreateClan`, the name and banner are applied, and the settlement affinity mirrors the parent clan.
5. **Family transfer**: Immediate family members apart from protected relatives are reassigned to the cadet branch.
6. **Tier balancing**: Renown adjustments ensure the branch meets the tier target determined by lineage.
7. **Notifications**: Campaign events and information messages inform the player base.

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
