#region Copyright & License Information
/*
 * Copyright 2007-2011 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made 
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.RA.Buildings;
using OpenRA.Mods.RA.Effects;
using OpenRA.Traits;
using OpenRA.Mods.RA.Activities;

namespace OpenRA.Mods.RA.Render
{
	public class RenderBuildingInfo : RenderSimpleInfo
	{
		public readonly bool HasMakeAnimation = true;
		public readonly float2 Origin = float2.Zero;
		public override object Create(ActorInitializer init) { return new RenderBuilding(init);}
		
		public override IEnumerable<Renderable> RenderPreview(ActorInfo building, string Tileset)
		{
            var rb = building.Traits.Get<RenderBuildingInfo>();
            return base.RenderPreview(building, Tileset)
                .Select(a => a.WithPos(a.Pos + rb.Origin));
		}
	}

	public class RenderBuilding : RenderSimple, INotifyDamage, INotifySold, IRenderModifier
	{
		readonly float2 Origin;
		
		public RenderBuilding( ActorInitializer init )
			: this( init, () => 0 )
		{
			Origin = init.self.Info.Traits.Get<RenderBuildingInfo>().Origin;
		}

		public IEnumerable<Renderable> ModifyRender(Actor self, IEnumerable<Renderable> r)
		{
			var disabled = self.TraitsImplementing<IDisable>().Any(d => d.Disabled);
			foreach (var a in r)
			{
				var ret = a.WithPos(a.Pos - Origin);
				yield return ret;
				if (disabled)
					yield return ret.WithPalette("disabled").WithZOffset(1);
			}
		}
		
		public RenderBuilding( ActorInitializer init, Func<int> baseFacing )
			: base(init.self, baseFacing)
		{
			var self = init.self;
			
			// Work around a bogus crash
			anim.PlayRepeating( NormalizeSequence(self, "idle") );
			
			if (self.Info.Traits.Get<RenderBuildingInfo>().HasMakeAnimation && !init.Contains<SkipMakeAnimsInit>())
				self.QueueActivity(new MakeAnimation(self));
			
			// Can't call Complete() from ctor because other traits haven't been inited yet
			self.QueueActivity(new CallFunc(() => self.World.AddFrameEndTask( _ => Complete( self ) )));
		}

		void Complete( Actor self )
		{
			anim.PlayRepeating( NormalizeSequence(self, "idle") );
			foreach( var x in self.TraitsImplementing<INotifyBuildComplete>() )
				x.BuildingComplete( self );
		}

		public void PlayCustomAnimThen(Actor self, string name, Action a)
		{
			anim.PlayThen(NormalizeSequence(self, name),
				() => { anim.PlayRepeating(NormalizeSequence(self, "idle")); a(); });
		}
		
		public void PlayCustomAnimRepeating(Actor self, string name)
		{
			anim.PlayThen(NormalizeSequence(self, name),
				() => { PlayCustomAnimRepeating(self, name); });
		}

		public void PlayCustomAnimBackwards(Actor self, string name, Action a)
		{
			anim.PlayBackwardsThen(NormalizeSequence(self, name),
				() => { anim.PlayRepeating(NormalizeSequence(self, "idle")); a(); });
		}

		public void CancelCustomAnim(Actor self)
		{
			anim.PlayRepeating( NormalizeSequence(self, "idle") );
		}
		
		public virtual void Damaged(Actor self, AttackInfo e)
		{
			if (!e.DamageStateChanged)
				return;
			
			if (e.DamageState == DamageState.Dead)
				foreach (var t in FootprintUtils.UnpathableTiles( self.Info.Name, self.Info.Traits.Get<BuildingInfo>(), self.Location ))
				{
					var cell = t; // required: c# fails at bindings
					self.World.AddFrameEndTask(w => w.Add(new Explosion(w, Traits.Util.CenterOfCell(cell), "building", false, 0)));
				}
			else if (e.DamageState >= DamageState.Heavy && e.PreviousDamageState < DamageState.Heavy)
				anim.ReplaceAnim("damaged-idle");
			else if (e.DamageState < DamageState.Heavy)
				anim.ReplaceAnim("idle");
		}

		public virtual void Selling( Actor self )
		{
			if( self.Info.Traits.Get<RenderBuildingInfo>().HasMakeAnimation )
				anim.PlayBackwardsThen( "make", null );
			
			foreach (var s in self.Info.Traits.Get<BuildingInfo>().SellSounds)
					Sound.PlayToPlayer(self.Owner, s, self.CenterLocation);
		}

		public void Sold(Actor self) {}
	}
}
