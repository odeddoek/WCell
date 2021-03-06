using WCell.Constants.GameObjects;
using WCell.Constants.Spells;
using WCell.Constants.Updates;
using WCell.Core.Initialization;
using WCell.RealmServer.Entities;
using WCell.RealmServer.Spells;
using WCell.RealmServer.Spells.Auras;
using WCell.RealmServer.Spells.Auras.Handlers;
using WCell.Constants;

namespace WCell.Addons.Default.Spells.Warlock
{
	public static class WarlockFixes
	{
		[Initialization(InitializationPass.Second)]
		public static void FixWarlock()
		{
			// Curse of Doom cannot be casted on Players and spawns a Demon on target death
			SpellLineId.WarlockCurseOfDoom.Apply(spell =>
			{
				spell.CanCastOnPlayer = false;
				spell.Effects[0].AuraEffectHandlerCreator = () => new SummonDoomguardOnDeathHandler();
			});

            // Armors are mutually exclusive
            AuraHandler.AddAuraGroup(SpellLineId.WarlockFelArmor, SpellLineId.WarlockDemonArmor, SpellLineId.WarlockDemonSkin);

            // can't have more than one of these per caster
            AuraHandler.AddAuraCasterGroup(
                SpellLineId.WarlockCurseOfTongues, SpellLineId.WarlockCurseOfTheElements,
                SpellLineId.WarlockCurseOfDoom, SpellLineId.WarlockCurseOfAgony,
                SpellLineId.WarlockCurseOfWeakness, SpellLineId.WarlockAfflictionCurseOfExhaustion);

            // Shadowflame DoT
            SpellHandler.Apply(spell => spell.AddTargetTriggerSpells(SpellId.Shadowflame_3), SpellId.ClassSkillShadowflameRank1);
            SpellHandler.Apply(spell => spell.AddTargetTriggerSpells(SpellId.Shadowflame_5), SpellId.ClassSkillShadowflameRank2);
            SpellHandler.Apply(spell => spell.Effects[0].ImplicitTargetA = ImplicitTargetType.ConeInFrontOfCaster, SpellId.Shadowflame_3);
            SpellHandler.Apply(spell => spell.Effects[0].ImplicitTargetA = ImplicitTargetType.ConeInFrontOfCaster, SpellId.Shadowflame_5);

            // Incinerate has extra damage if target has Immolate
            SpellLineId.WarlockIncinerate.Apply(spell =>
				spell.Effects[0].SpellEffectHandlerCreator = (cast, effect) => new IncreaseDamageIfAuraPresentHandler(cast, effect));

            // Demonic Circle Teleport
            var teleReqSpell = SpellHandler.AddCustomSpell(62388, "DemonicCircleTeleportRequirement");
            teleReqSpell.IsPreventionDebuff = false;
            teleReqSpell.AddAuraEffect(AuraType.Dummy);
            teleReqSpell.Attributes |= SpellAttributes.NoVisibleAura;
            teleReqSpell.Durations = new Spell.DurationEntry { Min = 360000, Max = 360000 };
            SpellHandler.Apply(spell =>
            {
                var efct = spell.AddEffect(SpellEffectType.Dummy);
                efct.MiscValue = (int)GOEntryId.DemonicCircleSummon;
                efct.SpellEffectHandlerCreator = (cast, effect) => new RecallToGOHandler(cast, effect);
                spell.AddCasterTriggerSpells(teleReqSpell);
            }, SpellId.ClassSkillDemonicCircleTeleport);

            // Demonic Circle Summon
            SpellHandler.Apply(spell => spell.AddCasterTriggerSpells(teleReqSpell.SpellId), SpellLineId.WarlockDemonicCircleSummon);
        }

        public class IncreaseDamageIfAuraPresentHandler : SpellEffectHandler
        {
            public IncreaseDamageIfAuraPresentHandler(SpellCast cast, SpellEffect effect)
                : base(cast, effect)
            {
            }

            protected override void Apply(WorldObject target)
            {
                var unit = target as Unit;
                var oldVal = Effect.BasePoints;

                if (unit != null && unit.Auras[SpellLineId.WarlockImmolate] != null)
                {
                    Effect.BasePoints += Effect.BasePoints / 4;
                }
                ((Unit)target).DoSpellDamage(m_cast.CasterUnit, Effect, CalcEffectValue());
                Effect.BasePoints = oldVal;
            }

            public override ObjectTypes TargetType
            {
                get { return ObjectTypes.Unit; }
            }
        }

        public class SummonDoomguardOnDeathHandler : PeriodicDamageHandler
        {
            protected override void Apply()
            {
                base.Apply();
                if (m_aura.Auras.Owner.YieldsXpOrHonor && !m_aura.Auras.Owner.IsAlive)
                {
                    m_aura.Auras.Owner.SpellCast.TriggerSelf(SpellId.ClassSkillCurseOfDoomEffect);
                }
            }
        }
    }
}