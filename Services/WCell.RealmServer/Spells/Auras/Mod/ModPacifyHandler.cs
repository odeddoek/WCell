using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WCell.RealmServer.Entities;
using WCell.Constants;
using WCell.Constants.Spells;

namespace WCell.RealmServer.Spells.Auras.Mod
{
	/// <summary>
	/// Prevents carrier from attacking or using "physical abilities"
	/// </summary>
	public class ModPacifyHandler : AuraEffectHandler
	{
		protected internal override void Apply()
		{
			Owner.Pacified++;
		}

		protected internal override void Remove(bool cancelled)
		{
			Owner.Pacified--;
		}
	}

	/// <summary>
	/// Prevents carrier from attacking or using "physical abilities"
	/// </summary>
	public class ModSilenceHandler : AuraEffectHandler
	{
		protected internal override void Apply()
		{
			Owner.IncMechanicCount(SpellMechanic.Silenced);
		}

		protected internal override void Remove(bool cancelled)
		{
			Owner.DecMechanicCount(SpellMechanic.Silenced);
		}
	}

	/// <summary>
	/// Prevents carrier from attacking or using "physical abilities"
	/// </summary>
	public class ModPacifySilenceHandler : AuraEffectHandler
	{
		protected internal override void Apply()
		{
			Owner.Pacified++;
			Owner.IncMechanicCount(SpellMechanic.Silenced);
		}

		protected internal override void Remove(bool cancelled)
		{
			Owner.Pacified--;
			Owner.DecMechanicCount(SpellMechanic.Silenced);
		}
	}
}